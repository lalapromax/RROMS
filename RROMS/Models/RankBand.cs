using System.ComponentModel.DataAnnotations;

namespace RROMS.Models;

/// 
/// One slice of the guest ranking (Top 1%, Top 5% ... Everyone else) with the kind message guests in it are shown.
/// A guest is in the first band, from the top, whose <see cref="UpToPercent"/> they fit under. The last band is always 100 and covers
/// everyone else. Administrators edit the wording and the cut-offs; messages never use words like "bottom" or "last" (see RankMessageRules).
/// 
public class RankBand
{
    public int Id { get; set; }

    /// Shown as "Top N%" on the scale. The last band is "Everyone".
    [Range(1, 100)]
    public int UpToPercent { get; set; }

    [Required, StringLength(240)]
    public string Message { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public string Label => UpToPercent >= 100 ? "Everyone" : $"Top {UpToPercent}%";
}

/// The standard seven bands, used for a fresh install and for "Restore the defaults" in Admin > Rewards.
public static class RankBandDefaults
{
    public static List<RankBand> Create() => new()
    {
        new RankBand { SortOrder = 1, UpToPercent = 1, Message = "You are one of our most devoted guests. Thank you, truly." },
        new RankBand { SortOrder = 2, UpToPercent = 5, Message = "You are in our top 5%. We notice every visit, and we are grateful." },
        new RankBand { SortOrder = 3, UpToPercent = 10, Message = "You are in our top 10%. Your table is always a pleasure to set." },
        new RankBand { SortOrder = 4, UpToPercent = 25, Message = "You are ahead of three in four of our guests. Come back soon." },
        new RankBand { SortOrder = 5, UpToPercent = 50, Message = "You are ahead of half of our guests. Thank you for coming back." },
        new RankBand { SortOrder = 6, UpToPercent = 80, Message = "Every visit counts, and yours are adding up. We are glad you are here." },
        new RankBand { SortOrder = 7, UpToPercent = 100, Message = "Thanks for being a loyal guest of ours. We look forward to your next visit." }
    };
}

/// The words a rank message may never contain. Checked on the server when an administrator saves.
public static class RankMessageRules
{
    public static readonly string[] Forbidden = { "bottom", "last", "lowest", "worst", "least", "behind" };

    public static bool IsKind(string? message, out string? word)
    {
        word = null;
        if (string.IsNullOrWhiteSpace(message)) return true;
        var text = " " + new string(message.ToLowerInvariant().Select(ch => char.IsLetter(ch) ? ch : ' ').ToArray()) + " ";
        foreach (var w in Forbidden)
        {
            if (text.Contains(" " + w + " ")) { word = w; return false; }
        }
        return true;
    }
}
