using System.ComponentModel.DataAnnotations;

namespace RROMS.Models;

public class Announcement
{
    public int Id { get; set; }

    [Required, StringLength(150)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(1000)]
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;

    public string? CreatedByAdminId { get; set; }
    public ApplicationUser? CreatedByAdmin { get; set; }
}
