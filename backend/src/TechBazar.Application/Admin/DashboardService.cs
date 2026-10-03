using Microsoft.EntityFrameworkCore;
using TechBazar.Application.Abstractions;
using TechBazar.Application.Orders;
using TechBazar.Domain.Entities;
using TechBazar.Domain.Enums;

namespace TechBazar.Application.Admin;

/// <summary>
/// Sales figures. "Sales" = orders that were not cancelled or returned. Money is summed in memory from a narrow projection of
/// the selected window (a few columns per order), which keeps the code identical on SQL Server and the SQLite test provider.
/// If order volume grows into the hundreds of thousands per window, move these aggregates into SQL / a reporting view.
/// </summary>
public sealed class DashboardService(IApplicationDbContext db, TimeProvider clock) : IDashboardService
{
    public async Task<DashboardDto> GetAsync(int days, int lowStockThreshold, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 365);
        var now = clock.GetUtcNow().UtcDateTime;
        var from = now.Date.AddDays(-(days - 1));

        // All aggregation happens in SQL: the work (and the rows transferred) is bounded by the number of days / top-N, not by how many
        // orders the shop has taken. Cancelled and returned orders are not sales.
        var dayRows = await DailySales(db.Orders.AsNoTracking(), from).ToListAsync(ct);
        var byDay = dayRows.ToDictionary(r => DateOnly.FromDateTime(r.Day), r => (r.Revenue, r.Orders));
        var series = Enumerable.Range(0, days).Select(i => DateOnly.FromDateTime(from.AddDays(i)))
            .Select(d => new DailySalesDto(d, byDay.TryGetValue(d, out var v) ? OrderCalculator.Round(v.Revenue) : 0m, byDay.TryGetValue(d, out v) ? v.Orders : 0)).ToList();

        var today = DateOnly.FromDateTime(now);
        var orderCount = dayRows.Sum(r => r.Orders);
        var revenue = OrderCalculator.Round(dayRows.Sum(r => r.Revenue));

        var statusCounts = (await db.Orders.AsNoTracking().GroupBy(o => o.Status).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct))
            .Select(x => new StatusCountDto(x.Key, x.N)).OrderBy(x => x.Status).ToList();

        var top = (await TopProducts(db.OrderItems.AsNoTracking(), from).ToListAsync(ct))
            .Select(x => new TopProductDto(x.ProductId, x.Name, x.Quantity, OrderCalculator.Round(x.Revenue))).ToList();

        var low = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.StockStatus == StockStatus.InStock && p.StockQuantity <= lowStockThreshold)
            .OrderBy(p => p.StockQuantity).ThenBy(p => p.Name).Take(20)
            .Select(p => new LowStockDto(p.Id, p.Name, p.Sku, p.Slug, p.StockQuantity)).ToListAsync(ct);

        var recent = await AdminOrderService.Project(db.Orders.AsNoTracking().OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).Take(8)).ToListAsync(ct);

        return new DashboardDto(
            days, revenue, orderCount, orderCount == 0 ? 0 : OrderCalculator.Round(revenue / orderCount),
            statusCounts.FirstOrDefault(s => s.Status == OrderStatus.Pending)?.Count ?? 0,
            byDay.TryGetValue(today, out var t) ? t.Orders : 0, byDay.TryGetValue(today, out t) ? OrderCalculator.Round(t.Revenue) : 0m,
            series, statusCounts, top, low, lowStockThreshold, recent);
    }

    internal sealed record DayRow(DateTime Day, decimal Revenue, int Orders);
    internal sealed record TopRow(int ProductId, string Name, int Quantity, decimal Revenue);

    /// <summary>Sales (not cancelled / returned) per UTC day since <paramref name="from"/>: one row per day, computed by the database.</summary>
    internal static IQueryable<DayRow> DailySales(IQueryable<Order> orders, DateTime from) =>
        orders.Where(o => o.CreatedAt >= from && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Returned)
            .GroupBy(o => o.CreatedAt.Date)
            .Select(g => new DayRow(g.Key, g.Sum(x => x.GrandTotal), g.Count()));

    /// <summary>Top five products by revenue over the same sales window: five rows, computed by the database.</summary>
    internal static IQueryable<TopRow> TopProducts(IQueryable<OrderItem> items, DateTime from) =>
        items.Where(i => i.Order.CreatedAt >= from && i.Order.Status != OrderStatus.Cancelled && i.Order.Status != OrderStatus.Returned)
            .GroupBy(i => i.ProductId)
            .OrderByDescending(g => g.Sum(x => x.LineTotal)).ThenBy(g => g.Key)   // rank and cut BEFORE projecting: ordering by a constructor-built row does not translate
            .Take(5)
            .Select(g => new TopRow(g.Key, g.Max(x => x.ProductName)!, g.Sum(x => x.Quantity), g.Sum(x => x.LineTotal)));
}
