namespace TechBazar.Domain.Enums;

public enum StockStatus { InStock = 1, OutOfStock = 2, PreOrder = 3, UpComing = 4 }

public enum OrderStatus { Pending = 1, Confirmed = 2, Processing = 3, Shipped = 4, Delivered = 5, Cancelled = 6, Returned = 7 }

public enum PaymentMethod { CashOnDelivery = 1, BKash = 2, Nagad = 3, Card = 4, BankTransfer = 5 }

public enum PaymentStatus { Unpaid = 1, Paid = 2, Refunded = 3, Failed = 4 }

public enum DiscountType { Percentage = 1, FixedAmount = 2 }

public static class Roles
{
    public const string Customer = "Customer";
    public const string Admin = "Admin";
}
