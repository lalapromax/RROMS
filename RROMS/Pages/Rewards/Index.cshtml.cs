using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Rewards;

// Customers earn, track and claim rewards. The folder rule in Program.cs lets only the Customer role in;
// staff and administrators have no rewards account.
public class IndexModel : PageModel
{
    private readonly IRewardService _rewardService;
    private readonly IRewardPerkService _perkService;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(IRewardService rewardService, IRewardPerkService perkService, UserManager<ApplicationUser> userManager)
    {
        _rewardService = rewardService;
        _perkService = perkService;
        _userManager = userManager;
    }

    public RewardStanding Standing { get; set; } = null!;
    public RankStanding Rank { get; set; } = null!;
    public List<TierCard> Cards { get; set; } = new();
    public List<RewardClaim> Claims { get; set; } = new();
    public List<LeaderboardRow> Leaderboard { get; set; } = new();
    public List<RewardLedgerEntry> History { get; set; } = new();
    public int PointsPerCurrencyUnit { get; set; }
    public string ShortName { get; set; } = "You";
    public string FirstName { get; set; } = "You";
    public DateTime Joined { get; set; }
    public bool ShowOnBoard { get; set; } = true;

    /// <summary>One group of perks on a tier card ("Welcome treats", "Every visit", "At the table").</summary>
    public record PerkGroup(string Name, List<PerkView> Perks);

    /// <summary>One tier card: where the guest is relative to it, the line it carries, and its perks grouped.</summary>
    public record TierCard(TierInfo Tier, int Index, string State, string Flag, string Message, string? Previous, List<PerkGroup> Groups, string Meta);

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostClaimAsync(int perkId)
    {
        var userId = _userManager.GetUserId(User)!;
        var (success, message) = await _perkService.ClaimAsync(userId, perkId);
        TempData[success ? "StatusMessage" : "ErrorMessage"] = message;
        return RedirectToPage(null, null, null, "tiers-h");
    }

    /// <summary>Whether the guest's first name and initial may appear on the top-ten board. Their own place is never hidden from them.</summary>
    public async Task<IActionResult> OnPostBoardAsync(bool show)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        user.ShowOnLeaderboard = show;
        await _userManager.UpdateAsync(user);
        TempData["StatusMessage"] = show ? "You are on the top-ten board when you reach it." : "Your name is off the board. Your own place is still yours to see.";
        return RedirectToPage(null, null, null, "where");
    }

    private async Task LoadAsync()
    {
        var userId = _userManager.GetUserId(User)!;
        var user = await _userManager.GetUserAsync(User);
        var parts = (user?.FullName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        FirstName = parts.FirstOrDefault() ?? "You";
        ShortName = parts.Length > 1 ? $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}." : FirstName;
        Joined = (user?.DateJoined ?? DateTime.UtcNow).ToLocalTime();
        ShowOnBoard = user?.ShowOnLeaderboard ?? true;

        var tiers = await _rewardService.GetTiersAsync();
        PointsPerCurrencyUnit = _rewardService.PointsPerCurrencyUnit;
        Standing = await _rewardService.GetStandingAsync(userId);
        Rank = await _rewardService.GetRankAsync(userId);
        var perks = await _perkService.GetPerksForCustomerAsync(userId);
        Claims = await _perkService.GetClaimsForCustomerAsync(userId, 10);
        Leaderboard = await _rewardService.GetLeaderboardAsync(userId, 10);
        History = await _rewardService.GetHistoryAsync(userId, 10);

        var points = Standing.Points;
        for (var i = 0; i < tiers.Count; i++)
        {
            var t = tiers[i];
            var state = t.MinPoints > points ? "locked" : t.Id == Standing.Tier.Id ? "here" : "done";
            var flag = state switch
            {
                "locked" => $"{t.MinPoints - points:N0} points to go",
                "here" => "You are here",
                _ => "Completed"
            };
            var raw = state switch { "locked" => t.MessageLocked, "here" => t.MessageCurrent, _ => t.MessagePassed };
            var message = (string.IsNullOrWhiteSpace(raw) ? t.Blurb : raw).Replace("{points}", t.MinPoints.ToString("N0"));

            var groups = perks.Where(p => p.Perk.RewardTierId == t.Id)
                .GroupBy(p => string.IsNullOrWhiteSpace(p.Perk.Group) ? "Welcome treats" : p.Perk.Group)
                .Select(g => new PerkGroup(g.Key, g.OrderBy(p => p.Perk.SortOrder).ThenBy(p => p.Perk.Title).ToList()))
                .ToList();

            var meta = t.MinPoints == 0 ? "From the first order" : $"From {t.MinPoints:N0} points";
            Cards.Add(new TierCard(t, i, state, flag, message, i > 0 ? tiers[i - 1].Name : null, groups, meta));
        }
    }
}
