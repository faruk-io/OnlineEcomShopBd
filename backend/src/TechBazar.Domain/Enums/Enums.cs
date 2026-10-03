namespace TechBazar.Domain.Enums;

public enum StockStatus { InStock = 1, OutOfStock = 2, PreOrder = 3, UpComing = 4 }

public enum OrderStatus { Pending = 1, Confirmed = 2, Processing = 3, Shipped = 4, Delivered = 5, Cancelled = 6, Returned = 7, ReadyForPickup = 8 }

public enum PaymentMethod { CashOnDelivery = 1, BKash = 2, Nagad = 3, Card = 4, BankTransfer = 5, Online = 6 }

public enum PaymentStatus { Unpaid = 1, Paid = 2, Refunded = 3, Failed = 4 }

public enum DiscountType { Percentage = 1, FixedAmount = 2 }

public static class Roles
{
    public const string Customer = "Customer";
    public const string Admin = "Admin";
}

public enum ShippingMethod { HomeDeliveryInsideDhaka = 1, HomeDeliveryOutsideDhaka = 2, StorePickup = 3 }

/// <summary>State of one payment attempt (an order can have several: a failed online attempt can be retried).</summary>
public enum PaymentAttemptStatus { Pending = 1, Paid = 2, Failed = 3, Cancelled = 4, Refunded = 5 }

/// <summary>Part slots of the PC Builder.</summary>
public enum BuildSlot { Cpu = 1, Motherboard = 2, Ram = 3, Storage = 4, Gpu = 5, Psu = 6, Case = 7, Cooler = 8, Monitor = 9 }

/// <summary>What a one-time emailed link may be used for. A token of one purpose is worthless for the other.</summary>
public enum AccountTokenPurpose { EmailVerification = 1, PasswordReset = 2 }
