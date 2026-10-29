using System.ComponentModel.DataAnnotations;

namespace RROMS.Models;

/// 
/// A past or ongoing partnership shown in the home page's "Collaborations", optionally with
/// one social post (an Instagram reel/post or a YouTube video) shown as a click-to-load embed.
/// Deliberately small: a name, a few optional links, an order, and a published switch.
/// 
public class Collaboration
{
    public int Id { get; set; }

    /// The partner's name.
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// One line about what was done together, e.g. "Seasonal tasting menu, spring 2026".
    [StringLength(200)]
    public string? Summary { get; set; }

    /// Partner logo or a photo from the collaboration: an uploaded /uploads/... file or an https address.
    [StringLength(300)]
    public string? ImageUrl { get; set; }

    /// The partner's website (http/https only - checked when saved).
    [StringLength(300)]
    public string? WebsiteUrl { get; set; }

    /// An Instagram reel/post or YouTube video about the collaboration (checked by <c>SocialEmbed</c> when saved).
    [StringLength(300)]
    public string? SocialUrl { get; set; }

    /// True for a partnership that is still running; false for a past one.
    public bool IsOngoing { get; set; }

    /// Year it started or took place (display only).
    [Range(1990, 2100)]
    public int? Year { get; set; }

    /// Lower numbers are shown first.
    public int DisplayOrder { get; set; }

    /// Only published entries appear on the public site.
    public bool IsPublished { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
