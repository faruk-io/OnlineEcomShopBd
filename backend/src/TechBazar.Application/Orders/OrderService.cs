using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechBazar.Application.Abstractions;
using TechBazar.Application.Common;
using TechBazar.Application.Email;
using TechBazar.Application.Payments;
using TechBazar.Domain.Enums;

namespace TechBazar.Application.Orders;

public sealed class OrderService(
    IApplicationDbContext db,
    OrderReader reader,
    IOrderFulfilment fulfilment,
    IPaymentService payments,
    IEmailSender email,
    IOptions<PaymentOptions> paymentOptions,
    ILogger<OrderService> logger) : IOrderService
{
    public async Task<PagedResult<OrderSummaryDto>> ListAsync(Guid userId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Orders.AsNoTracking().Where(o => o.UserId == userId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new OrderSummaryDto(
                o.OrderNumber, o.CreatedAt, o.Status, o.PaymentStatus, o.PaymentMethod, o.ShippingMethod, o.GrandTotal,
                o.Items.Sum(i => i.Quantity),
                o.Items.OrderBy(i => i.Id).Select(i => i.ProductName).FirstOrDefault(),
                o.Items.OrderBy(i => i.Id).Select(i => i.Product.Images.Where(x => x.IsPrimary).Select(x => x.Url).FirstOrDefault()).FirstOrDefault()))
            .ToListAsync(ct);
        return new PagedResult<OrderSummaryDto>(items, page, pageSize, total);
    }

    public Task<OrderDetailDto> GetAsync(Guid userId, string orderNumber, CancellationToken ct = default) =>
        reader.GetDetailAsync(o => o.UserId == userId && o.OrderNumber == orderNumber, ct);

    public async Task<OrderDetailDto> CancelAsync(Guid userId, string orderNumber, CancellationToken ct = default)
    {
        var order = await db.Orders.Include(o => o.Items).Include(o => o.Payments)
                        .FirstOrDefaultAsync(o => o.UserId == userId && o.OrderNumber == orderNumber, ct)
                    ?? throw new NotFoundException("Order not found.");
        if (!OrderStateMachine.CustomerCanCancel(order.Status))
            throw new ConflictException("This order can no longer be cancelled online. Please contact support.");

        await fulfilment.TransitionAsync(order, OrderStatus.Cancelled, "Cancelled by customer", userId, ct);
        var detail = await reader.GetDetailAsync(o => o.Id == order.Id, ct);
        try { await email.SendAsync(OrderEmails.StatusChanged(detail, paymentOptions.Value.StorefrontBaseUrl), ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not send cancellation email for {Order}", orderNumber); }
        return detail;
    }

    public async Task<PaymentRedirectDto> RetryPaymentAsync(Guid userId, string orderNumber, CancellationToken ct = default)
    {
        if (!payments.OnlinePaymentsEnabled) throw new ConflictException("Online payment is not available right now.");
        var order = await db.Orders.Include(o => o.Payments)
                        .FirstOrDefaultAsync(o => o.UserId == userId && o.OrderNumber == orderNumber, ct)
                    ?? throw new NotFoundException("Order not found.");
        if (order.Status != OrderStatus.Pending || order.PaymentMethod != PaymentMethod.Online || order.PaymentStatus == PaymentStatus.Paid)
            throw new ConflictException("This order cannot be paid online.");

        // Old unfinished attempts are closed so only one is live (a late success callback for them still counts).
        foreach (var stale in order.Payments.Where(p => p.Status == PaymentAttemptStatus.Pending)) stale.Status = PaymentAttemptStatus.Cancelled;
        var attempt = payments.CreateAttempt(order, PaymentMethod.Online);
        order.PaymentStatus = PaymentStatus.Unpaid;
        await db.SaveChangesAsync(ct);
        return await payments.StartOnlinePaymentAsync(order.OrderNumber, attempt.TransactionId, ct);
    }
}
