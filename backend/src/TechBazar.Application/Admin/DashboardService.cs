using Microsoft.EntityFrameworkCore;
using TechBazar.Application.Abstractions;
using TechBazar.Application.Orders;
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

        var window = await db.Orders.AsNoTracking().Where(o => o.CreatedAt >= from)
            .Select(o => new { o.Id, o.CreatedAt, o.GrandTotal, o.Status }).ToListAsync(ct);
        var sales = window.Where(o => o.Status is not (OrderStatus.Cancelled or OrderStatus.Returned)).ToList();

        var byDay = sales.GroupBy(o => DateOnly.FromDateTime(o.CreatedAt)).ToDictionary(g => g.Key, g => (Revenue: g.Sum(x => x.GrandTotal), Orders: g.Count()));
        var series = Enumerable.Range(0, days).Select(i => DateOnly.FromDateTime(from.AddDays(i)))
            .Select(d => new DailySalesDto(d, byDay.TryGetValue(d, out var v) ? OrderCalculator.Round(v.Revenue) : 0m, byDay.TryGetValue(d, out v) ? v.Orders : 0)).ToList();

        var today = DateOnly.FromDateTime(now);
        var revenue = OrderCalculator.Round(sales.Sum(o => o.GrandTotal));

        var statusCounts = (await db.Orders.AsNoTracking().GroupBy(o => o.Status).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct))
            .Select(x => new StatusCountDto(x.Key, x.N)).OrderBy(x => x.Status).ToList();

        var saleIds = sales.Select(o => o.Id).ToList();
        var lines = saleIds.Count == 0 ? [] : await db.OrderItems.AsNoTracking().Where(i => saleIds.Contains(i.OrderId))
            .Select(i => new { i.ProductId, i.ProductName, i.Quantity, i.LineTotal }).ToListAsync(ct);
        var top = lines.GroupBy(l => l.ProductId)
            .Select(g => new TopProductDto(g.Key, g.First().ProductName, g.Sum(x => x.Quantity), OrderCalculator.Round(g.Sum(x => x.LineTotal))))
            .OrderByDescending(t => t.Revenue).Take(5).ToList();

        var low = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.StockStatus == StockStatus.InStock && p.StockQuantity <= lowStockThreshold)
            .OrderBy(p => p.StockQuantity).ThenBy(p => p.Name).Take(20)
            .Select(p => new LowStockDto(p.Id, p.Name, p.Sku, p.Slug, p.StockQuantity)).ToListAsync(ct);

        var recent = await AdminOrderService.Project(db.Orders.AsNoTracking().OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).Take(8)).ToListAsync(ct);

        return new DashboardDto(
            days, revenue, sales.Count, sales.Count == 0 ? 0 : OrderCalculator.Round(revenue / sales.Count),
            statusCounts.FirstOrDefault(s => s.Status == OrderStatus.Pending)?.Count ?? 0,
            byDay.TryGetValue(today, out var t) ? t.Orders : 0, byDay.TryGetValue(today, out t) ? OrderCalculator.Round(t.Revenue) : 0m,
            series, statusCounts, top, low, lowStockThreshold, recent);
    }
}
