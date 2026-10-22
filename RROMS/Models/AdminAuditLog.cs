using System.ComponentModel.DataAnnotations;

namespace RROMS.Models;

/// 
/// An append-only record of sensitive administrative actions (deactivating an
/// account, changing someone's role, renaming the restaurant) - per the
/// brief's instruction to "record important administrative actions where
/// appropriate for accountability". Nothing ever reads or rewrites a row once
/// written; this is written to, never edited.
/// 
public class AdminAuditLog
{
    public int Id { get; set; }

    [Required]
    public string AdminUserId { get; set; } = string.Empty;
    public ApplicationUser? AdminUser { get; set; }

    /// Snapshot of the admin's name at the time of the action, so the log
    /// still reads sensibly even if the account is later renamed or deactivated.
    [Required, StringLength(100)]
    public string AdminNameSnapshot { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string Action { get; set; } = string.Empty;

    [StringLength(400)]
    public string? Details { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
