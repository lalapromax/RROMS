using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;

namespace RROMS.Services;

/// One perk as shown on a customer's Rewards page, with whether and why they can claim it right now.
public record PerkView(
    RewardPerk Perk,
    string TierName,
    string TierColor,
    int TierMinPoints,
    bool Unlocked,
    bool CanClaim,
    string? Note,
    string State = "locked",       // locked, always, claim, waiting, done (claimed, honoured, not available again), resting (available again later)
                                   // or held (the tier is reached but something else is missing first: a paid order, or the birthday month)
    DateTime? When = null,         // UTC: when the guest last claimed it
    DateTime? AvailableAgain = null);

public interface IRewardPerkService
{
    /// Every active perk, each marked unlocked/locked and claimable/not for this customer.
    Task<List<PerkView>> GetPerksForCustomerAsync(string customerId);

    Task<(bool Success, string Message)> ClaimAsync(string customerId, int perkId);

    Task<List<RewardClaim>> GetClaimsForCustomerAsync(string customerId, int take = 20);

    /// Moves a Waiting claim to Fulfilled or Declined and tells the customer.
    Task<(bool Success, string Message)> ResolveClaimAsync(int claimId, RewardClaimStatus newStatus, string adminId, string? note);
}

/// 
/// The claimable part of the loyalty scheme (adapted from MEDonate's perks + claims queue). A customer may claim a
/// perk when their lifetime-points tier is at or above the perk's tier and they have earned points from at least one
/// paid order (a brand-new account is Bronze but has not dined yet); birthday perks also wait for the birthday month.
/// The claim then waits for a person at the restaurant to fulfil or decline it. All checks (tier, earned points,
/// birthday, frequency, duplicate waiting claim) run here on the server.
/// 
public class RewardPerkService : IRewardPerkService
{
    private readonly ApplicationDbContext _context;
    private readonly IRewardService _rewards;
    private readonly INotificationService _notifications;

    public RewardPerkService(ApplicationDbContext context, IRewardService rewards, INotificationService notifications)
    {
        _context = context;
        _rewards = rewards;
        _notifications = notifications;
    }

    public async Task<List<PerkView>> GetPerksForCustomerAsync(string customerId)
    {
        var points = await _rewards.GetPointsAsync(customerId);
        var myTier = await _rewards.GetTierForPointsAsync(points);

        var perks = await _context.RewardPerks.AsNoTracking()
            .Include(p => p.Tier)
            .Where(p => p.IsActive && p.Tier != null)
            .ToListAsync();

        var myClaims = await _context.RewardClaims.AsNoTracking()
            .Where(c => c.CustomerId == customerId && c.Status != RewardClaimStatus.Declined && c.RewardPerkId != null)
            .ToListAsync();
        var birthMonth = await BirthMonthAsync(customerId);

        var now = DateTime.UtcNow;
        var views = new List<PerkView>();
        foreach (var perk in perks.OrderBy(p => p.Tier!.MinPoints).ThenBy(p => p.SortOrder).ThenBy(p => p.Title))
        {
            var unlocked = perk.Tier!.MinPoints <= myTier.MinPoints;
            var mine = myClaims.Where(c => c.RewardPerkId == perk.Id).OrderByDescending(c => c.CreatedAt).ToList();
            var last = mine.FirstOrDefault();
            string state; bool claimable = false; string? note = null; DateTime? again = null;

            if (!unlocked)
            {
                var missing = Math.Max(0, perk.Tier.MinPoints - points);
                state = "locked";
                note = $"Reach {perk.Tier.Name} ({missing:N0} more points) to unlock.";
            }
            else if (perk.Frequency == PerkFrequency.Always)
            {
                state = "always"; // nothing to claim; it simply applies
            }
            else if (NotYet(perk, points, birthMonth) is string wait && !mine.Any(c => c.Status == RewardClaimStatus.Waiting))
            {
                state = "held";
                note = wait;
            }
            else
            {
                var (blocked, reason) = BlockedReason(perk, mine, now);
                claimable = !blocked;
                note = reason;
                if (!blocked) state = "claim";
                else if (mine.Any(c => c.Status == RewardClaimStatus.Waiting)) state = "waiting";
                else
                {
                    again = perk.Frequency switch
                    {
                        PerkFrequency.Monthly => new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1),
                        PerkFrequency.Yearly => new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddYears(1),
                        _ => null
                    };
                    state = again == null ? "done" : "resting";
                }
            }

            views.Add(new PerkView(perk, perk.Tier.Name, perk.Tier.ColorHex, perk.Tier.MinPoints, unlocked, claimable, note, state,
                state is "waiting" or "done" or "resting" ? last?.CreatedAt : null, again));
        }
        return views;
    }

    public async Task<(bool Success, string Message)> ClaimAsync(string customerId, int perkId)
    {
        await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        var perk = await _context.RewardPerks.Include(p => p.Tier).FirstOrDefaultAsync(p => p.Id == perkId);
        if (perk == null || !perk.IsActive || perk.Tier == null)
            return (false, "That perk isn't available any more.");

        var points = await _rewards.GetPointsAsync(customerId);
        var myTier = await _rewards.GetTierForPointsAsync(points);
        if (perk.Tier.MinPoints > myTier.MinPoints)
            return (false, $"This perk unlocks at {perk.Tier.Name}.");
        if (perk.Frequency == PerkFrequency.Always)
            return (false, "There is nothing to claim here. This one is always on for you.");
        if (NotYet(perk, points, await BirthMonthAsync(customerId)) is string wait)
            return (false, wait);

        var myClaims = await _context.RewardClaims
            .Where(c => c.CustomerId == customerId && c.RewardPerkId == perkId && c.Status != RewardClaimStatus.Declined)
            .ToListAsync();
        var (blocked, reason) = BlockedReason(perk, myClaims, DateTime.UtcNow);
        if (blocked)
            return (false, reason ?? "You can't claim this perk right now.");

        var customer = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == customerId);
        _context.RewardClaims.Add(new RewardClaim
        {
            RewardPerkId = perk.Id,
            CustomerId = customerId,
            PerkTitle = perk.Title,
            TierName = myTier.Name,
            Status = RewardClaimStatus.Waiting,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        await tx.CommitAsync();

        await _notifications.NotifyAdministratorsAsync(
            "New reward claim",
            $"{customer?.FullName ?? "A customer"} ({myTier.Name}) claimed \"{perk.Title}\".",
            NotificationType.System,
            "/Admin/Rewards?tab=claims");

        return (true, $"Claimed! We'll let you know when \"{perk.Title}\" is ready.");
    }

    public async Task<List<RewardClaim>> GetClaimsForCustomerAsync(string customerId, int take = 20)
    {
        return await _context.RewardClaims.AsNoTracking()
            .Where(c => c.CustomerId == customerId)
            .OrderByDescending(c => c.CreatedAt)
            .Take(take)
            .ToListAsync();
    }

    public async Task<(bool Success, string Message)> ResolveClaimAsync(int claimId, RewardClaimStatus newStatus, string adminId, string? note)
    {
        if (newStatus != RewardClaimStatus.Fulfilled && newStatus != RewardClaimStatus.Declined)
            return (false, "Choose Fulfilled or Declined.");

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note != null && note.Length > 300)
            return (false, "Keep the note under 300 characters.");
        if (newStatus == RewardClaimStatus.Declined && note == null)
            return (false, "Please add a short note so the customer knows why the claim was declined.");

        var claim = await _context.RewardClaims.FirstOrDefaultAsync(c => c.Id == claimId);
        if (claim == null)
            return (false, "Claim not found.");
        if (claim.Status != RewardClaimStatus.Waiting)
            return (false, "This claim has already been handled.");

        claim.Status = newStatus;
        claim.ResolvedAt = DateTime.UtcNow;
        claim.ResolvedById = adminId;
        claim.AdminNote = note;
        await _context.SaveChangesAsync();

        if (newStatus == RewardClaimStatus.Fulfilled)
        {
            await _notifications.NotifyAsync(claim.CustomerId, "Your reward is ready",
                $"\"{claim.PerkTitle}\" has been approved. Mention it to our team on your next visit.{(note != null ? " " + note : "")}",
                NotificationType.Reward, "/Rewards/Index");
        }
        else
        {
            await _notifications.NotifyAsync(claim.CustomerId, "About your reward claim",
                $"We couldn't fulfil \"{claim.PerkTitle}\". {note}",
                NotificationType.Reward, "/Rewards/Index");
        }

        return (true, newStatus == RewardClaimStatus.Fulfilled ? "Marked as fulfilled and the customer was notified." : "Declined and the customer was notified.");
    }

    private Task<int?> BirthMonthAsync(string customerId) =>
        _context.Users.AsNoTracking().Where(u => u.Id == customerId).Select(u => u.BirthMonth).FirstOrDefaultAsync();

    /// Why a perk the guest's tier unlocks still cannot be claimed today, or null when nothing stands in the way.
    /// Points only come from paid, completed orders, so none means the guest has not dined with us yet.
    private static string? NotYet(RewardPerk perk, int points, int? birthMonth)
    {
        if (points <= 0)
            return "Opens after your first paid visit. Points from that order unlock your perks.";
        if (perk.BirthdayOnly)
        {
            if (birthMonth is not (>= 1 and <= 12))
                return "Add your birthday to your profile to claim this in your birthday month.";
            if (DateTime.Today.Month != birthMonth)
                return $"Claim it in your birthday month, {System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(birthMonth.Value)}.";
        }
        return null;
    }

    /// Applies the perk's frequency rule to a customer's earlier (non-declined) claims of the same perk.
    private static (bool Blocked, string? Reason) BlockedReason(RewardPerk perk, List<RewardClaim> earlierClaims, DateTime nowUtc)
    {
        if (earlierClaims.Any(c => c.Status == RewardClaimStatus.Waiting))
            return (true, "Your claim is waiting for the team.");

        switch (perk.Frequency)
        {
            case PerkFrequency.Once:
                return earlierClaims.Count > 0 ? (true, "You've already claimed this one-time perk.") : (false, null);

            case PerkFrequency.Monthly:
            {
                var thisMonth = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                if (earlierClaims.Any(c => c.CreatedAt >= thisMonth))
                    return (true, $"Already claimed this month - available again {thisMonth.AddMonths(1):d MMM}.");
                return (false, null);
            }

            case PerkFrequency.Yearly:
            {
                var thisYear = new DateTime(nowUtc.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                if (earlierClaims.Any(c => c.CreatedAt >= thisYear))
                    return (true, $"Already claimed this year - available again {thisYear.AddYears(1):d MMM yyyy}.");
                return (false, null);
            }

            default:
                return (false, null);
        }
    }
}
