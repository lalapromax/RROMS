using RROMS.Models;
using RROMS.Services;

namespace RROMS.Presentation;

/// 
/// How an announcement is described in the staff and admin lists: its state (Draft, Scheduled, Published, Ended),
/// who it goes to, and when. Kept in one place so both lists say the same thing.
/// 
public static class AnnouncementLabels
{
    public static string State(Announcement a, DateTime nowUtc)
    {
        if (!a.IsActive) return "Draft";
        if (a.PublishAt.HasValue && a.PublishAt > nowUtc) return "Scheduled";
        if (a.ExpiresAt.HasValue && a.ExpiresAt < nowUtc) return "Ended";
        return "Published";
    }

    /// The status-pill class from the shared stylesheet.
    public static string Css(string state) => state switch
    {
        "Published" => "st-published",
        "Scheduled" => "st-pending",
        "Ended" => "st-completed",
        _ => "st-draft"
    };

    public static string Audience(Announcement a, IReadOnlyDictionary<int, string> tierNames)
    {
        if (a.IsPublic) return "Everyone";
        var parts = new List<string>();
        if (a.ToAllCustomers) parts.Add("All customers");
        if (a.ToAllStaff) parts.Add("All staff");
        var tiers = a.GetTierIds().Select(id => tierNames.TryGetValue(id, out var n) ? n : null).Where(n => n != null).ToList();
        if (tiers.Count > 0) parts.Add(JoinNames(tiers!) + " members");
        if (a.Recipients.Count > 0) parts.Add(a.Recipients.Count == 1 ? "1 named guest" : $"{a.Recipients.Count} named guests");
        return string.Join(", ", parts);
    }

    /// "Today, 09:10", "5 Nov, 19:00", or "Scheduled for 22 Oct" for something not yet live.
    public static string When(Announcement a, DateTime nowUtc)
    {
        if (a.IsActive && a.PublishAt.HasValue && a.PublishAt > nowUtc)
            return "Scheduled for " + TimeInput.Display(a.PublishAt, "d MMM, HH:mm");
        var at = a.PublishAt ?? a.CreatedAt;
        return Stamp(at, nowUtc);
    }

    public static string Stamp(DateTime utc, DateTime nowUtc)
    {
        var local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        var today = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc).ToLocalTime().Date;
        return local.Date == today ? "Today, " + local.ToString("HH:mm") : local.ToString("d MMM, HH:mm");
    }

    private static string JoinNames(IReadOnlyList<string> names) =>
        names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
}
