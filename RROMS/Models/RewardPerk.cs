using System.ComponentModel.DataAnnotations;

namespace RROMS.Models;

/// How often one customer may claim the same perk.
public enum PerkFrequency
{
    Once = 0,       // one time, ever
    Monthly = 1,    // once per calendar month
    Yearly = 2,     // once per calendar year
    Always = 3      // always on from the tier it unlocks at; nothing to claim (for example points on every order)
}

/// 
/// Something a customer can claim once they have reached <see cref="RewardTierId"/> (or any higher tier).
/// A claim goes to the restaurant's queue (Admin > Rewards) and is honoured by a person - nothing is
/// granted automatically, so a perk must be something the restaurant can really give.
/// 
public class RewardPerk
{
    public int Id { get; set; }

    /// The lowest tier that unlocks this perk. Higher tiers keep it too.
    public int RewardTierId { get; set; }
    public RewardTier? Tier { get; set; }

    [Required, StringLength(80)]
    public string Title { get; set; } = string.Empty;

    [StringLength(300)]
    public string? Description { get; set; }

    /// A short icon key (e.g. "glass"). Checked against a short allow-list; PerkIcons.Normalize repairs old keys.
    [StringLength(30)]
    public string Icon { get; set; } = "gift";

    /// The heading the perk sits under on the guest Rewards page, for example "Welcome treats", "Every visit" or "At the table".
    [StringLength(40)]
    public string Group { get; set; } = "Welcome treats";

    public PerkFrequency Frequency { get; set; } = PerkFrequency.Monthly;

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    /// A birthday treat ("Birthday dessert", "Birthday glass of champagne"): claimable only in the guest's birthday month,
    /// so it needs the birthday on their profile. Set by the administrator on the perk.
    public bool BirthdayOnly { get; set; }
}

/// The icons an administrator may pick for a perk, stored as short keys that name drawings in the shared icon set
/// (<c>Ui.Icon</c>). A short fixed list keeps a typo from turning into a missing icon and keeps arbitrary text out of the markup.
public static class PerkIcons
{
    public static readonly string[] All =
    {
        "gift", "cake", "glass", "bottle", "crown", "cal", "clock", "star", "book", "table", "fork", "tag", "bag", "receipt"
    };

    // Keys saved by earlier versions keep working.
    private static readonly Dictionary<string, string> Legacy = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cup-straw"] = "glass", ["cup-hot"] = "glass", ["cake2"] = "cake", ["basket"] = "bag", ["egg-fried"] = "fork",
        ["percent"] = "tag", ["ticket-perforated"] = "tag", ["stars"] = "star", ["heart"] = "star", ["award"] = "crown",
        ["trophy"] = "crown", ["calendar-check"] = "cal", ["emoji-smile"] = "gift", ["bag-heart"] = "bag", ["fire"] = "star", ["gem"] = "crown"
    };

    public static string Normalize(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return "gift";
        if (All.Contains(icon, StringComparer.OrdinalIgnoreCase)) return icon.ToLowerInvariant();
        return Legacy.TryGetValue(icon, out var mapped) ? mapped : "gift";
    }
}
