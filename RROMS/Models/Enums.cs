namespace RROMS.Models;

public enum ReservationStatus
{
    Pending = 0,
    Confirmed = 1,
    Seated = 2,
    Completed = 3,
    Cancelled = 4,
    NoShow = 5
}

public enum OrderType
{
    DineIn = 0,
    Takeaway = 1
}

public enum OrderStatus
{
    Placed = 0,
    Preparing = 1,
    ReadyToServe = 2,
    Served = 3,
    Completed = 4,
    Cancelled = 5
}

public enum PaymentMethod
{
    Cash = 0,
    Card = 1,
    /// Card paid through the Stripe form on the cart page.
    OnlinePayment = 2,
    /// Bank transfer through the guest's own bank (Stripe FPX). Settled online like <see cref="OnlinePayment"/>.
    OnlineBanking = 3,
    /// A QR code shown at the table and scanned with the guest's own banking app. Recorded by staff once it shows as received.
    QrPayment = 4
}

public enum PaymentStatus
{
    Pending = 0,
    Paid = 1,
    Failed = 2,
    Refunded = 3
}

public enum NotificationType
{
    Reservation = 0,
    Order = 1,
    Payment = 2,
    Promotion = 3,
    System = 4,
    /// News and events from the restaurant.
    Announcement = 5,
    /// Points earned, tier changes and reward claims.
    Reward = 6
}

/// 
/// Central place for ASP.NET Core Identity role names used throughout the app.
/// 
public static class Roles
{
    public const string Administrator = "Administrator";
    public const string Staff = "Staff";
    public const string Customer = "Customer";

    public static readonly string[] All = { Administrator, Staff, Customer };
}

/// 
/// Shared list of common dietary restrictions for dropdown inputs (staff
/// profile, and later the customer profile) - a single source of truth so
/// the same options appear everywhere one is needed, with "Other" always
/// paired with a free-text field for anything not on the list. Allergies are
/// intentionally NOT part of this dropdown: the brief asks that allergies be
/// recorded and displayed separately from general dietary preferences.
/// 
public static class DietaryOptions
{
    public const string Other = "Other";

    public static readonly string[] Common =
    {
        "None",
        "Vegetarian",
        "Vegan",
        "Halal",
        "Kosher",
        "No pork",
        "Gluten-Free",
        "Dairy-Free",
        "Pescatarian",
        Other
    };
}
