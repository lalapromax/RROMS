using System.ComponentModel.DataAnnotations;

namespace RROMS.Models;

/// 
/// A customer review (brief 6.H). One review per completed order, or per completed dining
/// experience (reservation) that had no order of its own - enforced by unique database
/// indexes, not just by the form.
///
/// The scores are deliberately separate columns rather than one free-text blob so the admin
/// dashboard can average and trend them. Every score except <see cref="Rating"/> is nullable:
/// null means "not answered" (for example, no waiting time to rate on a takeaway), and
/// reviews written before these questions existed simply have nulls.
/// 
public class Feedback
{
    public int Id { get; set; }

    [Required]
    public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser? Customer { get; set; }

    public int? OrderId { get; set; }
    public Order? Order { get; set; }

    public int? ReservationId { get; set; }
    public Reservation? Reservation { get; set; }

    /// Overall satisfaction, 1-5 (the original rating column - kept so old reviews still count).
    [Range(1, 5)]
    public int Rating { get; set; }

    [Range(1, 5)] public int? FoodRating { get; set; }
    [Range(1, 5)] public int? ServiceRating { get; set; }
    [Range(1, 5)] public int? CleanlinessRating { get; set; }

    /// 1 = far too long, 5 = no waiting at all.
    [Range(1, 5)] public int? WaitTimeRating { get; set; }
    [Range(1, 5)] public int? ValueRating { get; set; }

    /// "How likely are you to recommend us?", 0-10 (net promoter style).
    [Range(0, 10)]
    public int? RecommendScore { get; set; }

    /// Comma-separated tags from <see cref="ReviewOptions.IssueTags"/> picked when something went wrong.
    [StringLength(300)]
    public string? IssueTags { get; set; }

    [StringLength(1000)]
    public string? Comments { get; set; }

    /// Comma-separated site-relative URLs of uploaded review photos (server-generated, at most <see cref="ReviewOptions.MaxImages"/>).
    [StringLength(600)]
    public string? ImagePaths { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// The guest ticked "Show my first name with my review". When off, the team sees the review as from "a guest".
    public bool ShowFirstName { get; set; } = true;

    /// A guest can change or take back their own review for this many days after writing it.
    public const int GuestEditDays = 7;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public DateTime EditableUntil => CreatedAt.AddDays(GuestEditDays);

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool GuestCanEdit => DateTime.UtcNow < EditableUntil;

    [StringLength(600)]
    public string? StaffResponse { get; set; }

    public string? RespondedByStaffId { get; set; }
    public ApplicationUser? RespondedByStaff { get; set; }
    public DateTime? RespondedAt { get; set; }

    public IReadOnlyList<string> ImageList() =>
        string.IsNullOrWhiteSpace(ImagePaths)
            ? Array.Empty<string>()
            : ImagePaths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public IReadOnlyList<string> IssueTagList() =>
        string.IsNullOrWhiteSpace(IssueTags)
            ? Array.Empty<string>()
            : IssueTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// Fixed lists and limits shared by the review form, its server-side validation and the admin dashboard.
public static class ReviewOptions
{
    public const int MaxImages = 3;
    public const int MaxCommentLength = 1000;

    /// 
    /// A fixed list (rather than free text) is what lets the dashboard answer "what goes wrong most often?"; the comment box
    /// stays for the details. Older reviews may carry tags from an earlier list, which still display.
    /// 
    public static readonly string[] IssueTags =
    {
        "Long wait for food",
        "Slow service",
        "Food arrived cold",
        "Too noisy",
        "Not worth the price",
        "Wrong or missing item"
    };

    /// Metric key -&gt; (label shown in the form and on the dashboard, question asked).
    public static readonly (string Key, string Label, string Question)[] Metrics =
    {
        ("food", "Food quality", "How did the dishes taste and look?"),
        ("service", "Service", "Were we attentive and kind?"),
        ("cleanliness", "Cleanliness and atmosphere", "The room, the tables, the music."),
        ("wait", "Waiting time", "From sitting down to the plates arriving."),
        ("value", "Value for money", "Did it feel worth the price?")
    };
}
