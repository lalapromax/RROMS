using RROMS.Models;

namespace RROMS.Presentation;

/// Turns an audit-log row into a sentence an administrator reads at a glance: "You terminated Noor H.".
public static class AuditLabels
{
    public static string Describe(AdminAuditLog log, string? currentUserId)
    {
        var who = log.AdminUserId == currentUserId ? "You" : log.AdminNameSnapshot;
        var subject = Subject(log.Details);
        return log.Action switch
        {
            "AddStaff" => $"{who} added {subject} as Staff",
            "ReactivateStaff" => $"{who} added {subject} back as Staff",
            "TerminateStaff" => $"{who} terminated {subject}",
            "ChangeOperatingHours" => $"{who} updated the opening hours",
            "ChangeSettings" => $"{who} updated the restaurant settings",
            "UpdateStaffDietaryInfo" => $"{who} updated the dietary information of {subject}",
            "PublishAnnouncement" => $"{who} published “{log.Details}”",
            "ReplyToReview" => $"{who} replied to a guest review",
            "AddPromotion" => $"{who} added the promotion “{log.Details}”",
            "EditPromotion" => $"{who} changed the promotion “{log.Details}”",
            "EndPromotion" => $"{who} ended the promotion “{log.Details}”",
            "SwitchOffPromotion" => $"{who} switched off the promotion “{log.Details}”",
            "DeletePromotion" => $"{who} removed the promotion “{log.Details}”",
            "AddRewardTier" => $"{who} added a reward tier ({log.Details})",
            "EditRewardTier" => $"{who} changed a reward tier ({log.Details})",
            "DeleteRewardTier" => $"{who} deleted the {log.Details} tier",
            "AddRewardPerk" => $"{who} added the perk “{log.Details}”",
            "EditRewardPerk" => $"{who} changed the perk “{log.Details}”",
            "DeleteRewardPerk" => $"{who} deleted the perk “{log.Details}”",
            "FulfilRewardClaim" => $"{who} fulfilled a reward claim ({log.Details})",
            "DeclineRewardClaim" => $"{who} declined a reward claim ({log.Details})",
            "EditRankMessages" => $"{who} changed the ranking and tier messages",
            "RestoreRankBands" => $"{who} restored the standard ranking bands",
            "CreateCollaboration" => $"{who} added the collaboration “{log.Details}”",
            "UpdateCollaboration" => $"{who} changed the collaboration “{log.Details}”",
            "ExportMonitoring" => $"{who} downloaded a record ({log.Details})",
            "DeleteCollaboration" => $"{who} removed the collaboration “{log.Details}”",
            "ChangeRole" => $"{who} changed a role",          // older entries from before roles were locked
            _ => $"{who}: {Humanise(log.Action)}" + (string.IsNullOrWhiteSpace(log.Details) ? "" : $" ({log.Details})")
        };
    }

    /// "Today, 09:12", "Yesterday, 18:40" or "15 Oct, 11:05" (local time).
    public static string When(DateTime utc)
    {
        var local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        var today = DateTime.Now.Date;
        return local.Date == today ? "Today, " + local.ToString("HH:mm")
            : local.Date == today.AddDays(-1) ? "Yesterday, " + local.ToString("HH:mm")
            : local.ToString("d MMM, HH:mm");
    }

    /// The name part of "Mei L. (mei@x.com)".
    private static string Subject(string? details)
    {
        if (string.IsNullOrWhiteSpace(details)) return "an account";
        var at = details.IndexOf(" (", StringComparison.Ordinal);
        return at > 0 ? details[..at] : details;
    }

    private static string Humanise(string action)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < action.Length; i++)
        {
            if (i > 0 && char.IsUpper(action[i])) sb.Append(' ');
            sb.Append(i == 0 ? char.ToLowerInvariant(action[i]) : char.ToLowerInvariant(action[i]));
        }
        return sb.ToString();
    }
}
