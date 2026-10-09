using System.ComponentModel.DataAnnotations;

namespace RROMS.Models;

/// <summary>
/// A past or ongoing partnership shown in the home page's "Collaborations" strip (brief 6.J), optionally with
/// one social post (an Instagram reel/post or a YouTube video) shown as a click-to-load embed.
/// Deliberately small: a name, a few optional links, an order, and a published switch.
/// </summary>
public class Collaboration
{
    public int Id { get; set; }

    /// <summary>The partner's name.</summary>
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>One line about what was done together, e.g. "Seasonal tasting menu, spring 2026".</summary>
    [StringLength(200)]
    public string? Summary { get; set; }

    /// <summary>Partner logo or a photo from the collaboration: an uploaded /uploads/... file or an https address.</summary>
    [StringLength(300)]
    public string? ImageUrl { get; set; }

    /// <summary>The partner's website (http/https only - checked when saved).</summary>
    [StringLength(300)]
    public string? WebsiteUrl { get; set; }

    /// <summary>An Instagram reel/post or YouTube video about the collaboration (checked by <c>SocialEmbed</c> when saved).</summary>
    [StringLength(300)]
    public string? SocialUrl { get; set; }

    /// <summary>True for a partnership that is still running; false for a past one.</summary>
    public bool IsOngoing { get; set; }

    /// <summary>Year it started or took place (display only).</summary>
    [Range(1990, 2100)]
    public int? Year { get; set; }

    /// <summary>Lower numbers are shown first.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Only published entries appear on the public site.</summary>
    public bool IsPublished { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
