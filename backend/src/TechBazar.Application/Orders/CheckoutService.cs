using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechBazar.Application.Abstractions;
using TechBazar.Application.Common;
using TechBazar.Application.Email;
using TechBazar.Application.Payments;
using TechBazar.Application.Shopping;
using TechBazar.Domain.Entities;
using TechBazar.Domain.Enums;

namespace TechBazar.Application.Orders;

public sealed class CheckoutService(
    IApplicationDbContext db,
    IPaymentService payments,
    OrderReader reader,
    IEmailSender email,
    IOptions<ShippingOptions> shippingOptions,
    IOptions<PaymentOptions> paymentOptions,
    IOptions<TechBazar.Application.Auth.AccountOptions> accountOptions,
    TimeProvider clock,
    ILogger<CheckoutService> logger) : ICheckoutService
{
    private readonly ShippingOptions _ship = shippingOptions.Value;

    public CheckoutOptionsDto Options() => new(
        [
            new ShippingOptionDto(ShippingMethod.HomeDeliveryInsideDhaka, "Home delivery · inside Dhaka", _ship.InsideDhakaFee, "Usually within 1–2 working days"),
            new ShippingOptionDto(ShippingMethod.HomeDeliveryOutsideDhaka, "Home delivery · outside Dhaka", _ship.OutsideDhakaFee, "Courier delivery in 2–5 working days"),
            new ShippingOptionDto(ShippingMethod.StorePickup, "Store pickup", _ship.PickupFee, $"Collect from {_ship.StoreName}"),
        ],
        [
            new PaymentOptionDto(PaymentMethod.CashOnDelivery, "Cash on delivery", "Pay in cash when you receive (or collect) your order", true),
            new PaymentOptionDto(PaymentMethod.Online, "Pay online", "bKash, Nagad, cards and internet banking via SSLCommerz", payments.OnlinePaymentsEnabled),
        ],
        new StoreDto(_ship.StoreName, _ship.StoreAddress),
        Divisions.All,
        accountOptions.Value.RequireVerifiedEmailForCheckout);

    // ------------------------------------------------------------------ quote
    public async Task<CheckoutQuoteDto> QuoteAsync(Guid userId, CheckoutQuoteRequest request, CancellationToken ct = default)
    {
        var p = await PrepareAsync(userId, request.ShippingMethod, request.AddressId, request.CouponCode, ct);
        var problems = new List<string>(p.Problems);
        if (!ShippingRules.IsPickup(request.ShippingMethod) && p.Address is null) problems.Add("Choose a delivery address.");
        return new CheckoutQuoteDto(
            p.Lines, p.Totals.Subtotal, p.Totals.Discount, p.Method, p.Totals.ShippingFee, p.Totals.GrandTotal,
            p.Totals.Coupon is { Code.Length: > 0 } c ? c.Code : null, p.Totals.Coupon?.Applied ?? false, p.Totals.Coupon?.Error,
            problems.Count == 0 && p.Lines.Count > 0, problems);
    }

    // ------------------------------------------------------------------ place
    public async Task<PlaceOrderResult> PlaceAsync(Guid userId, string email, PlaceOrderRequest request, CancellationToken ct = default)
    {
        if (request.PaymentMethod == PaymentMethod.Online && !payments.OnlinePaymentsEnabled)
            throw new ConflictException("Online payment is not available right now. Please choose cash on delivery.");

        string orderNumber;
        string? onlineTransaction = null;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                (orderNumber, onlineTransaction) = await TryPlaceAsync(userId, email, request, ct);
                break;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                // Someone else bought the last unit / used the last coupon between our read and write: re-read and re-validate.
                db.ChangeTracker.Clear();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConflictException("Stock changed while you were checking out. Please review your cart and try again.");
            }
        }

        PaymentRedirectDto? redirect = null;
        if (onlineTransaction is not null) redirect = await payments.StartOnlinePaymentAsync(orderNumber, onlineTransaction, ct);

        var detail = await reader.GetDetailAsync(o => o.OrderNumber == orderNumber, ct);
        await SafeSendAsync(OrderEmails.Confirmation(detail, paymentOptions.Value.StorefrontBaseUrl), ct);
        return new PlaceOrderResult(detail, redirect);
    }

    private async Task<(string OrderNumber, string? OnlineTransaction)> TryPlaceAsync(Guid userId, string contactEmail, PlaceOrderRequest request, CancellationToken ct)
    {
        var p = await PrepareAsync(userId, request.ShippingMethod, request.AddressId, request.CouponCode, ct);
        if (p.CartItems.Count == 0) throw new ConflictException("Your cart is empty.");
        if (p.Problems.Count > 0) throw new ConflictException(string.Join(" ", p.Problems));
        if (!string.IsNullOrWhiteSpace(request.CouponCode) && p.Totals.Coupon is { Applied: false } bad) throw new ConflictException(bad.Error ?? "Coupon cannot be applied.");

        var pickup = ShippingRules.IsPickup(p.Method);
        if (!pickup && p.Address is null) throw Validation(nameof(request.AddressId), "Choose a delivery address.");

        var now = clock.GetUtcNow().UtcDateTime;
        var order = new Order
        {
            OrderNumber = await NewOrderNumberAsync(now, ct),
            UserId = userId,
            ContactEmail = contactEmail,
            Status = OrderStatus.Pending,
            ShippingMethod = p.Method,
            PaymentMethod = request.PaymentMethod,
            PaymentStatus = PaymentStatus.Unpaid,
            Subtotal = p.Totals.Subtotal,
            DiscountTotal = p.Totals.Discount,
            ShippingFee = p.Totals.ShippingFee,
            GrandTotal = p.Totals.GrandTotal,
            CouponCode = p.Totals.Coupon is { Applied: true } c ? c.Code : null,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            ShipFullName = pickup ? request.ContactName!.Trim() : p.Address!.FullName,
            ShipPhone = pickup ? request.ContactPhone!.Trim() : p.Address!.Phone,
            ShipDivision = pickup ? "Dhaka" : p.Address!.Division,
            ShipDistrict = pickup ? "Dhaka" : p.Address!.District,
            ShipUpazila = pickup ? null : p.Address!.Upazila,
            ShipAddressLine = pickup ? $"Store pickup: {_ship.StoreName}, {_ship.StoreAddress}" : p.Address!.AddressLine,
            ShipPostalCode = pickup ? null : p.Address!.PostalCode,
        };
        order.History.Add(new OrderStatusHistory { Order = order, Status = OrderStatus.Pending, Note = "Order placed" });

        foreach (var ci in p.CartItems)
        {
            var product = ci.Product;
            var unit = product.EffectivePrice; // the price RIGHT NOW, never the cart snapshot or anything from the browser
            var tracksStock = product.StockStatus == StockStatus.InStock;
            order.Items.Add(new OrderItem
            {
                Order = order, ProductId = product.Id, ProductName = product.Name, Sku = product.Sku,
                UnitPrice = unit, Quantity = ci.Quantity, LineTotal = OrderCalculator.Round(unit * ci.Quantity), StockDecremented = tracksStock,
            });
            if (tracksStock)
            {
                product.StockQuantity -= ci.Quantity;
                if (product.StockQuantity <= 0) product.StockStatus = StockStatus.OutOfStock;
            }
            product.SoldCount += ci.Quantity;
        }

        if (p.Coupon is not null && order.CouponCode is not null) p.Coupon.UsedCount++;

        var attemptRow = payments.CreateAttempt(order, request.PaymentMethod);
        db.Orders.Add(order);
        db.CartItems.RemoveRange(p.CartItems);
        await db.SaveChangesAsync(ct);

        return (order.OrderNumber, request.PaymentMethod == PaymentMethod.Online ? attemptRow.TransactionId : null);
    }

    // ------------------------------------------------------------------ shared preparation
    private sealed class Prepared
    {
        public List<CartItem> CartItems { get; init; } = [];
        public List<CheckoutLineDto> Lines { get; init; } = [];
        public List<string> Problems { get; init; } = [];
        public Address? Address { get; init; }
        public ShippingMethod Method { get; init; }
        public Coupon? Coupon { get; init; }
        public OrderTotals Totals { get; init; } = default!;
    }

    private async Task<Prepared> PrepareAsync(Guid userId, ShippingMethod requested, int? addressId, string? couponCode, CancellationToken ct)
    {
        var items = await db.CartItems.Include(i => i.Product)
            .Where(i => i.Cart.UserId == userId).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).ToListAsync(ct);

        var ids = items.Select(i => i.ProductId).ToList();
        var images = await db.ProductImages.AsNoTracking().Where(i => ids.Contains(i.ProductId) && i.IsPrimary)
            .Select(i => new { i.ProductId, i.Url }).ToListAsync(ct);
        var imageBy = images.GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.First().Url);

        var lines = new List<CheckoutLineDto>();
        var problems = new List<string>();
        var priced = new List<PricedLine>();
        foreach (var i in items)
        {
            var pr = i.Product;
            string? problem = null;
            int? available = null;
            if (!pr.IsActive || pr.StockStatus is StockStatus.OutOfStock or StockStatus.UpComing)
                problem = $"{pr.Name} is no longer available.";
            else if (pr.StockStatus == StockStatus.InStock)
            {
                available = Math.Max(pr.StockQuantity, 0);
                if (pr.StockQuantity < i.Quantity)
                    problem = pr.StockQuantity <= 0 ? $"{pr.Name} just sold out." : $"Only {pr.StockQuantity} of {pr.Name} left in stock.";
            }
            if (problem is not null) problems.Add(problem);
            else priced.Add(new PricedLine(pr.Id, pr.EffectivePrice, i.Quantity));
            lines.Add(new CheckoutLineDto(pr.Id, pr.Slug, pr.Name, imageBy.GetValueOrDefault(pr.Id), pr.EffectivePrice, i.Quantity,
                OrderCalculator.Round(pr.EffectivePrice * i.Quantity), pr.StockStatus, available, problem));
        }

        Address? address = null;
        if (!ShippingRules.IsPickup(requested) && addressId is { } aid)
            address = await db.Addresses.AsNoTracking().FirstOrDefaultAsync(a => a.Id == aid && a.UserId == userId, ct)
                      ?? throw new NotFoundException("Address not found.");

        var method = ShippingRules.Resolve(requested, address?.District);
        var fee = ShippingRules.Fee(method, _ship);

        Coupon? coupon = null;
        CouponTerms? terms = null;
        var code = couponCode?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(code))
        {
            coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Code == code, ct);
            if (coupon is not null)
                terms = new CouponTerms(coupon.Code, coupon.DiscountType, coupon.Value, coupon.MinOrderAmount, coupon.MaxDiscountAmount,
                    coupon.UsageLimit, coupon.UsedCount, coupon.StartsAt, coupon.ExpiresAt, coupon.IsActive);
        }

        var totals = OrderCalculator.Calculate(priced, fee, terms, code, clock.GetUtcNow().UtcDateTime);
        return new Prepared { CartItems = items, Lines = lines, Problems = problems, Address = address, Method = method, Coupon = coupon, Totals = totals };
    }

    private async Task<string> NewOrderNumberAsync(DateTime now, CancellationToken ct)
    {
        for (var i = 0; i < 5; i++)
        {
            var number = $"TB-{now:yyMMdd}-{PaymentService.RandomCode(5)}";
            if (!await db.Orders.IgnoreQueryFilters().AnyAsync(o => o.OrderNumber == number, ct)) return number;
        }
        throw new InvalidOperationException("Could not allocate an order number.");
    }

    private async Task SafeSendAsync(EmailMessage message, CancellationToken ct)
    {
        try { await email.SendAsync(message, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not send email '{Subject}'", message.Subject); } // the order is already placed
    }

    private static ValidationException Validation(string field, string message) => new([new ValidationFailure(field, message)]);
}
