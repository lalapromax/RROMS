using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RROMS.Models;

public class Announcement
{
    public int Id { get; set; }

    [Required, StringLength(150)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(1000)]
    public string Content { get; set; } = string.Empty;

    [StringLength(300)]
    public string? ImageUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// When this announcement should start showing up for customers.
    /// Null means immediately (as soon as IsActive is true).
    public DateTime? PublishAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    /// The admin's publish/unpublish switch - false hides it regardless
    /// of PublishAt/ExpiresAt.
    public bool IsActive { get; set; } = true;

    /// Shown first, large, at the top of the Notices page.
    public bool IsPinned { get; set; }

    /// Optional button on the pinned notice, e.g. "Reserve a seat".
    [StringLength(40)]
    public string? ButtonText { get; set; }

    /// Where that button goes: a page of this site (starts with /).
    [StringLength(200)]
    public string? ButtonUrl { get; set; }

    /// 
    /// True while guests are still to be told about this announcement (it was scheduled for later, and the notice goes out
    /// the moment it goes live). The background dispatcher clears it after sending, so a notice is never sent twice.
    /// 
    public bool NotifyPending { get; set; }

    /// Who wrote it - an administrator or a staff member (the property name is historical).
    public string? CreatedByAdminId { get; set; }
    public ApplicationUser? CreatedByAdmin { get; set; }

    /// "Administrator" or "Staff". Staff are limited to customer audiences; the server enforces this.
    [StringLength(20)]
    public string CreatedByRole { get; set; } = Roles.Administrator;

    // ---------------- Audience ----------------
    /// Every customer account.
    public bool ToAllCustomers { get; set; }

    /// Every staff account.
    public bool ToAllStaff { get; set; }

    /// Comma-separated RewardTier ids: customers who are currently in any of these tiers.
    [StringLength(200)]
    public string? ToTierIds { get; set; }

    /// Named individual people (customers or staff) picked by an administrator.
    public ICollection<AnnouncementRecipient> Recipients { get; set; } = new List<AnnouncementRecipient>();

    [NotMapped]
    public bool IsPublic => !ToAllCustomers && !ToAllStaff && string.IsNullOrWhiteSpace(ToTierIds) && Recipients.Count == 0;

    public List<int> GetTierIds() =>
        (ToTierIds ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, out var n) ? n : 0)
            .Where(n => n > 0)
            .Distinct()
            .ToList();

    [NotMapped]
    public bool IsCurrentlyVisible =>
        IsActive &&
        (PublishAt == null || PublishAt <= DateTime.UtcNow) &&
        (ExpiresAt == null || ExpiresAt >= DateTime.UtcNow);

    [NotMapped]
    public string StatusLabel =>
        !IsActive ? "Hidden" :
        PublishAt.HasValue && PublishAt > DateTime.UtcNow ? "Scheduled" :
        ExpiresAt.HasValue && ExpiresAt < DateTime.UtcNow ? "Expired" :
        "Active";
}

/// One named person an announcement was addressed to.
public class AnnouncementRecipient
{
    public int Id { get; set; }

    public int AnnouncementId { get; set; }
    public Announcement? Announcement { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
}
