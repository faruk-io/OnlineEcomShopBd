using TechBazar.Domain.Enums;

namespace TechBazar.Application.Orders;

/// <summary>Bound from the "Shipping" configuration section; defaults are plausible BDT charges.</summary>
public sealed class ShippingOptions
{
    public const string SectionName = "Shipping";
    public decimal InsideDhakaFee { get; set; } = 70m;
    public decimal OutsideDhakaFee { get; set; } = 130m;
    public decimal PickupFee { get; set; } = 0m;
    public string StoreName { get; set; } = "TechBazar BD Store";
    public string StoreAddress { get; set; } = "Store address placeholder, Dhaka";
}

public static class ShippingRules
{
    public const string DhakaDistrict = "Dhaka";

    public static bool IsInsideDhaka(string? district) =>
        string.Equals(district?.Trim(), DhakaDistrict, StringComparison.OrdinalIgnoreCase);

    public static bool IsPickup(ShippingMethod method) => method == ShippingMethod.StorePickup;

    /// <summary>
    /// Home delivery is priced by the ADDRESS, not by what the client claims: a Chattogram address cannot buy the
    /// inside-Dhaka rate by sending the "inside Dhaka" method.
    /// </summary>
    public static ShippingMethod Resolve(ShippingMethod requested, string? district) =>
        IsPickup(requested) ? ShippingMethod.StorePickup
        : IsInsideDhaka(district) ? ShippingMethod.HomeDeliveryInsideDhaka
        : ShippingMethod.HomeDeliveryOutsideDhaka;

    public static decimal Fee(ShippingMethod method, ShippingOptions o) => method switch
    {
        ShippingMethod.HomeDeliveryInsideDhaka => o.InsideDhakaFee,
        ShippingMethod.HomeDeliveryOutsideDhaka => o.OutsideDhakaFee,
        ShippingMethod.StorePickup => o.PickupFee,
        _ => throw new ArgumentOutOfRangeException(nameof(method)),
    };
}
