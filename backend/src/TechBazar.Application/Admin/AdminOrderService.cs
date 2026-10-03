using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechBazar.Application.Abstractions;
using TechBazar.Application.Catalog;
using TechBazar.Application.Common;
using TechBazar.Application.Email;
using TechBazar.Application.Orders;
using TechBazar.Application.Payments;
using TechBazar.Domain.Enums;

namespace TechBazar.Application.Admin;

public sealed class AdminOrderService(
    IApplicationDbContext db, OrderReader reader, IOrderFulfilment fulfilment, IEmailSender email,
    IOptions<PaymentOptions> paymentOptions, ILogger<AdminOrderService> logger) : IAdminOrderService
{
    public async Task<PagedResult<AdminOrderListItemDto>> ListAsync(OrderStatus? status, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var q = db.Orders.AsNoTracking().AsQueryable();
        if (status is { } s) q = q.Where(o => o.Status == s);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = TextSearch.Contains(search.Trim());
            q = q.Where(o => EF.Functions.Like(o.OrderNumber, pattern, TextSearch.Escape) || EF.Functions.Like(o.ShipFullName, pattern, TextSearch.Escape)
                             || EF.Functions.Like(o.ShipPhone, pattern, TextSearch.Escape) || EF.Functions.Like(o.ContactEmail, pattern, TextSearch.Escape));
        }
        var total = await q.CountAsync(ct);
        var items = await Project(q.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(ct);
        return new PagedResult<AdminOrderListItemDto>(items, page, pageSize, total);
    }

    internal static IQueryable<AdminOrderListItemDto> Project(IQueryable<Domain.Entities.Order> q) =>
        q.Select(o => new AdminOrderListItemDto(o.OrderNumber, o.CreatedAt, o.ShipFullName, o.ShipPhone, o.ContactEmail, o.Status, o.PaymentStatus,
            o.PaymentMethod, o.ShippingMethod, o.GrandTotal, o.Items.Sum(i => i.Quantity)));

    public async Task<AdminOrderDetailDto> GetAsync(string orderNumber, CancellationToken ct = default)
    {
        var detail = await reader.GetDetailAsync(o => o.OrderNumber == orderNumber, ct);
        return Wrap(detail);
    }

    public async Task<AdminOrderDetailDto> UpdateStatusAsync(string orderNumber, UpdateOrderStatusRequest request, Guid adminId, CancellationToken ct = default)
    {
        var order = await db.Orders.Include(o => o.Items).Include(o => o.Payments).FirstOrDefaultAsync(o => o.OrderNumber == orderNumber, ct)
                    ?? throw new NotFoundException("Order not found.");
        await fulfilment.TransitionAsync(order, request.Status, string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(), adminId, ct);

        var detail = await reader.GetDetailAsync(o => o.Id == order.Id, ct);
        try { await email.SendAsync(OrderEmails.StatusChanged(detail, paymentOptions.Value.StorefrontBaseUrl), ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not send status email for {Order}", orderNumber); }
        return Wrap(detail);
    }

    public async Task<AdminOrderDetailDto> MarkPaidAsync(string orderNumber, Guid adminId, CancellationToken ct = default)
    {
        var order = await db.Orders.Include(o => o.Payments).FirstOrDefaultAsync(o => o.OrderNumber == orderNumber, ct)
                    ?? throw new NotFoundException("Order not found.");
        if (order.PaymentStatus == PaymentStatus.Paid) throw new ConflictException("This order is already paid.");
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Returned) throw new ConflictException("A cancelled or returned order cannot be marked as paid.");

        order.PaymentStatus = PaymentStatus.Paid;
        foreach (var p in order.Payments.Where(p => p.Status == PaymentAttemptStatus.Pending)) { p.Status = PaymentAttemptStatus.Paid; p.PaidAt = DateTime.UtcNow; }
        db.OrderStatusHistories.Add(new Domain.Entities.OrderStatusHistory { OrderId = order.Id, Order = order, Status = order.Status, Note = "Payment recorded manually by an administrator", ChangedByUserId = adminId });
        await db.SaveChangesAsync(ct);
        return Wrap(await reader.GetDetailAsync(o => o.Id == order.Id, ct));
    }

    private static AdminOrderDetailDto Wrap(OrderDetailDto d) =>
        new(d, OrderStateMachine.Next(d.Status, d.ShippingMethod), d.PaymentStatus != PaymentStatus.Paid && d.Status is not (OrderStatus.Cancelled or OrderStatus.Returned));
}
