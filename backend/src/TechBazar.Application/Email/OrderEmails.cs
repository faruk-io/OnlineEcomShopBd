using System.Net;
using System.Text;
using TechBazar.Application.Orders;
using TechBazar.Application.Common;
using TechBazar.Domain.Enums;

namespace TechBazar.Application.Email;

/// <summary>Plain, dependency-free email bodies (HTML is escaped; every value comes from the stored order).</summary>
public static class OrderEmails
{
    public static EmailMessage Confirmation(OrderDetailDto o, string storefrontBaseUrl)
    {
        var url = $"{storefrontBaseUrl.TrimEnd('/')}/account/orders/{o.OrderNumber}";
        var delivery = o.Pickup is not null
            ? $"Pick up from: {o.Pickup.Name}, {o.Pickup.Address}"
            : $"Deliver to: {o.ShipTo.FullName}, {o.ShipTo.AddressLine}, {o.ShipTo.District}, {o.ShipTo.Division} ({o.ShipTo.Phone})";
        var payment = o.PaymentMethod == PaymentMethod.CashOnDelivery ? "Cash on delivery" : "Online payment";

        var text = new StringBuilder()
            .AppendLine($"Thank you for your order, {o.ShipTo.FullName}!")
            .AppendLine($"Order number: {o.OrderNumber}")
            .AppendLine()
            .AppendLine("Items:");
        foreach (var i in o.Items) text.AppendLine($"  {i.Quantity} x {i.Name} — {Bdt(i.LineTotal)}");
        text.AppendLine()
            .AppendLine($"Subtotal: {Bdt(o.Subtotal)}");
        if (o.DiscountTotal > 0) text.AppendLine($"Discount ({o.CouponCode}): -{Bdt(o.DiscountTotal)}");
        text.AppendLine($"Delivery: {Bdt(o.ShippingFee)}")
            .AppendLine($"Total: {Bdt(o.GrandTotal)}")
            .AppendLine($"Payment: {payment}")
            .AppendLine(delivery)
            .AppendLine()
            .AppendLine($"Track your order: {url}");

        var html = new StringBuilder($"<h2>Thank you for your order!</h2><p>Order <strong>{H(o.OrderNumber)}</strong></p><table>");
        foreach (var i in o.Items) html.Append($"<tr><td>{i.Quantity} × {H(i.Name)}</td><td align=\"right\">{H(Bdt(i.LineTotal))}</td></tr>");
        html.Append($"</table><p>Subtotal {H(Bdt(o.Subtotal))}");
        if (o.DiscountTotal > 0) html.Append($" · Discount −{H(Bdt(o.DiscountTotal))}");
        html.Append($" · Delivery {H(Bdt(o.ShippingFee))}<br/><strong>Total {H(Bdt(o.GrandTotal))}</strong> ({H(payment)})</p><p>{H(delivery)}</p><p><a href=\"{H(url)}\">Track your order</a></p>");

        return new EmailMessage(o.ContactEmail, $"Order {o.OrderNumber} received - TechBazar BD", text.ToString(), html.ToString());
    }

    public static EmailMessage PaymentReceived(OrderDetailDto o, string storefrontBaseUrl) => new(
        o.ContactEmail, $"Payment received for order {o.OrderNumber} - TechBazar BD",
        $"We received your payment of {Bdt(o.GrandTotal)} for order {o.OrderNumber}. Track it: {storefrontBaseUrl.TrimEnd('/')}/account/orders/{o.OrderNumber}");

    public static EmailMessage StatusChanged(OrderDetailDto o, string storefrontBaseUrl) => new(
        o.ContactEmail, $"Order {o.OrderNumber}: {OrderReader.Label(o.Status, o.Pickup is not null)} - TechBazar BD",
        $"Your order {o.OrderNumber} is now: {OrderReader.Label(o.Status, o.Pickup is not null)}. Track it: {storefrontBaseUrl.TrimEnd('/')}/account/orders/{o.OrderNumber}");

    private static string Bdt(decimal v) => Money.Bdt(v);
    private static string H(string s) => WebUtility.HtmlEncode(s);
}
