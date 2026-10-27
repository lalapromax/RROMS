using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace RROMS.Models;

public class ApplicationUser : IdentityUser
{
    /// 
    /// The one shared account that bookings and orders taken at the door or by phone are filed under. It cannot sign in (inactive, no password).
    /// The real name and number of the guest live on the reservation itself (ContactName, ContactPhone).
    /// 
    public const string WalkInEmail = "walk-in@house.invalid";

    /// Accounts that are not a real inbox: the walk-in account and closed accounts. Nothing is ever sent to them.
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsSystemAccount => Email != null && Email.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase);

    [PersonalData]
    public string FullName { get; set; } = string.Empty;

    [PersonalData]
    public string? Address { get; set; }

    [PersonalData]
    public string? DietaryPreference { get; set; }

    /// 
    /// Allergies, stored and displayed separately from DietaryPreference per
    /// the brief - a preference ("vegetarian") and an allergy ("peanuts") are
    /// different things with different stakes, so they are never merged into
    /// one free-text field.
    /// 
    [PersonalData]
    public string? Allergies { get; set; }

    /// 
    /// Site-relative path of the uploaded profile picture (e.g. /uploads/profiles/abc.jpg),
    /// or null to show the default initials avatar.
    /// 
    [StringLength(300)]
    public string? ProfileImagePath { get; set; }

    public DateTime DateJoined { get; set; } = DateTime.UtcNow;

    /// 
    /// Allows an Administrator to deactivate a staff or customer account without deleting it.
    /// 
    public bool IsActive { get; set; } = true;

    /// Set when an administrator terminates a staff account. The account is kept (never deleted) so past work stays attributed.
    public DateTime? TerminatedAt { get; set; }

    /// Guests can choose to stay off the "top guests" board on the Rewards page.
    public bool ShowOnLeaderboard { get; set; } = true;

    /// Birthday (month and day only), so the restaurant can set aside a glass for the birthday week. Never shown to other guests.
    public int? BirthMonth { get; set; }
    public int? BirthDay { get; set; }

    // ---- How we reach the guest (Notices page and Profile). Booking, order and payment updates always arrive. ----
    /// News and events from the restaurant.
    public bool NoticeAnnouncements { get; set; } = true;
    /// Points, tier changes and reward claims.
    public bool NoticeRewards { get; set; } = true;
    /// Promotions and offers. Off until the guest asks for them.
    public bool NoticeOffers { get; set; }
    /// Also send the notices above (and booking and order updates) by email.
    public bool NoticeByEmail { get; set; } = true;

    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<Feedback> Feedbacks { get; set; } = new List<Feedback>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}
