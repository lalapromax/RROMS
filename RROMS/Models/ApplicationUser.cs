using Microsoft.AspNetCore.Identity;

namespace RROMS.Models;

public class ApplicationUser : IdentityUser
{
    [PersonalData]
    public string FullName { get; set; } = string.Empty;

    [PersonalData]
    public string? Address { get; set; }

    [PersonalData]
    public string? DietaryPreference { get; set; }

    public DateTime DateJoined { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Allows an Administrator to deactivate a staff or customer account without deleting it.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<Feedback> Feedbacks { get; set; } = new List<Feedback>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}
