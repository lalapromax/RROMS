using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Admin;

/// 
/// Admin > Rewards: edit the tiers (names, point thresholds, colours), edit the perks each tier unlocks, work through
/// the claims customers have made, and word the kind messages guests see about their tier and ranking.
/// Every change is validated here on the server and written to the audit log.
/// 
public class RewardsModel : PageModel
{
    private static readonly Regex HexColour = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    private readonly ApplicationDbContext _context;
    private readonly IRewardService _rewards;
    private readonly IRewardPerkService _perks;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditService _audit;

    public RewardsModel(ApplicationDbContext context, IRewardService rewards, IRewardPerkService perks, UserManager<ApplicationUser> userManager, IAuditService audit)
    {
        _context = context;
        _rewards = rewards;
        _perks = perks;
        _userManager = userManager;
        _audit = audit;
    }

    public List<RewardTier> Tiers { get; set; } = new();
    public List<RankBand> Bands { get; set; } = new();
    public Dictionary<int, int> MemberCounts { get; set; } = new();
    public List<RewardClaim> Claims { get; set; } = new();
    public Dictionary<RewardClaimStatus, int> ClaimCounts { get; set; } = new();
    public int TotalCustomers { get; set; }
    public List<string> PerkGroups { get; set; } = new();

    /// "tiers" (default), "claims" or "ranking".
    [BindProperty(SupportsGet = true)] public string? Tab { get; set; }
    [BindProperty(SupportsGet = true)] public RewardClaimStatus? Status { get; set; }

    public string TabKey => Tab?.ToLowerInvariant() switch { "claims" => "claims", "ranking" => "ranking", _ => "tiers" };
    public bool ClaimsTab => TabKey == "claims";
    public bool RankingTab => TabKey == "ranking";
    public RewardClaimStatus ActiveStatus => Status ?? RewardClaimStatus.Waiting;
    public int WaitingClaims => ClaimCounts.TryGetValue(RewardClaimStatus.Waiting, out var n) ? n : 0;

    public async Task OnGetAsync()
    {
        Tiers = await _context.RewardTiers.Include(t => t.Perks).OrderBy(t => t.MinPoints).ToListAsync();
        foreach (var t in Tiers) t.Perks = t.Perks.OrderBy(p => p.SortOrder).ThenBy(p => p.Title).ToList();

        PerkGroups = new[] { "Welcome treats", "Every visit", "At the table" }
            .Concat(Tiers.SelectMany(t => t.Perks).Select(p => p.Group))
            .Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // How many customers sit in each tier right now (computed from their points, never stored).
        var customerIds = (await _userManager.GetUsersInRoleAsync(Roles.Customer)).Where(u => u.IsActive).Select(u => u.Id).ToList();
        TotalCustomers = customerIds.Count;
        var totals = await _context.RewardLedger
            .GroupBy(r => r.CustomerId)
            .Select(g => new { CustomerId = g.Key, Points = g.Sum(x => x.Points) })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Points);
        foreach (var id in customerIds)
        {
            totals.TryGetValue(id, out var points);
            var tier = Tiers.Where(t => points >= t.MinPoints).OrderByDescending(t => t.MinPoints).FirstOrDefault() ?? Tiers.FirstOrDefault();
            if (tier == null) continue;
            MemberCounts[tier.Id] = MemberCounts.TryGetValue(tier.Id, out var n) ? n + 1 : 1;
        }

        foreach (var s in Enum.GetValues<RewardClaimStatus>())
            ClaimCounts[s] = await _context.RewardClaims.CountAsync(c => c.Status == s);

        if (ClaimsTab)
        {
            var wanted = ActiveStatus;
            Claims = await _context.RewardClaims.Include(c => c.Customer)
                .Where(c => c.Status == wanted)
                .OrderBy(c => wanted == RewardClaimStatus.Waiting ? c.CreatedAt : DateTime.MaxValue)
                .ThenByDescending(c => c.ResolvedAt ?? c.CreatedAt)
                .Take(100)
                .ToListAsync();
        }

        if (RankingTab)
            Bands = await _context.RankBands.AsNoTracking().OrderBy(b => b.UpToPercent).ThenBy(b => b.SortOrder).ToListAsync();
    }

    // ------------------------------------------------------------------ tiers
    public async Task<IActionResult> OnPostSaveTierAsync(int? id, string name, int minPoints, string? blurb, string? colorHex)
    {
        name = (name ?? string.Empty).Trim();
        blurb = string.IsNullOrWhiteSpace(blurb) ? null : blurb.Trim();
        colorHex = (colorHex ?? string.Empty).Trim();

        if (name.Length == 0 || name.Length > 40) return Fail("Give the tier a name (up to 40 characters).");
        if (blurb != null && blurb.Length > 200) return Fail("Keep the tier description under 200 characters.");
        if (minPoints < 0 || minPoints > 10_000_000) return Fail("Points needed must be between 0 and 10,000,000.");
        if (!HexColour.IsMatch(colorHex)) return Fail("Pick a colour in the form #RRGGBB.");

        var others = await _context.RewardTiers.Where(t => t.Id != (id ?? 0)).ToListAsync();
        if (others.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))) return Fail("Another tier already has that name.");
        if (others.Any(t => t.MinPoints == minPoints)) return Fail("Another tier already starts at that many points. Each tier needs its own threshold.");

        RewardTier tier;
        if (id.HasValue && id.Value != 0)
        {
            var existing = await _context.RewardTiers.FindAsync(id.Value);
            if (existing == null) return Fail("That tier no longer exists.");
            tier = existing;
        }
        else
        {
            tier = new RewardTier();
            _context.RewardTiers.Add(tier);
        }

        // The ladder needs a floor: somebody with zero points has to land in a tier.
        var lowest = others.Concat(new[] { new RewardTier { MinPoints = minPoints } }).Min(t => t.MinPoints);
        if (lowest != 0) return Fail("The lowest tier must start at 0 points, so every new customer belongs to a tier.");

        var wasNew = tier.Id == 0;
        tier.Name = name;
        tier.MinPoints = minPoints;
        tier.Blurb = blurb;
        tier.ColorHex = colorHex.ToUpperInvariant();
        await _context.SaveChangesAsync();
        _rewards.InvalidateTierCache();

        await LogAsync(wasNew ? "AddRewardTier" : "EditRewardTier", $"{name} from {minPoints} points");
        TempData["StatusMessage"] = wasNew ? $"{name} tier added." : $"{name} tier saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteTierAsync(int id)
    {
        var tier = await _context.RewardTiers.Include(t => t.Perks).FirstOrDefaultAsync(t => t.Id == id);
        if (tier == null) return RedirectToPage();
        if (tier.MinPoints == 0) return Fail("The starting tier (0 points) can't be deleted.");
        if (tier.Perks.Any()) return Fail("Move or delete this tier's perks first, then delete the tier.");

        _context.RewardTiers.Remove(tier);
        await _context.SaveChangesAsync();
        _rewards.InvalidateTierCache();
        await LogAsync("DeleteRewardTier", tier.Name);
        TempData["StatusMessage"] = $"{tier.Name} tier deleted.";
        return RedirectToPage();
    }

    // ------------------------------------------------------------------ perks
    public async Task<IActionResult> OnPostSavePerkAsync(int? id, int tierId, string title, string? description, string? icon, string? group, PerkFrequency frequency, bool isActive, bool birthdayOnly)
    {
        title = (title ?? string.Empty).Trim();
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        group = string.IsNullOrWhiteSpace(group) ? "Welcome treats" : group.Trim();

        if (title.Length == 0 || title.Length > 80) return Fail("Give the perk a title (up to 80 characters).");
        if (description != null && description.Length > 300) return Fail("Keep the perk description under 300 characters.");
        if (group.Length > 40) return Fail("Keep the heading under 40 characters.");
        if (!Enum.IsDefined(frequency)) return Fail("Choose how often the perk can be claimed.");
        if (!await _context.RewardTiers.AnyAsync(t => t.Id == tierId)) return Fail("Choose which tier unlocks this perk.");

        RewardPerk perk;
        if (id.HasValue && id.Value != 0)
        {
            var existing = await _context.RewardPerks.FindAsync(id.Value);
            if (existing == null) return Fail("That perk no longer exists.");
            perk = existing;
        }
        else
        {
            perk = new RewardPerk { SortOrder = await _context.RewardPerks.Where(p => p.RewardTierId == tierId).CountAsync() + 1 };
            _context.RewardPerks.Add(perk);
        }

        var wasNew = perk.Id == 0;
        perk.RewardTierId = tierId;
        perk.Title = title;
        perk.Description = description;
        perk.Icon = PerkIcons.Normalize(icon);
        perk.Group = group;
        perk.Frequency = frequency;
        perk.IsActive = isActive;
        perk.BirthdayOnly = birthdayOnly && frequency != PerkFrequency.Always;   // an always-on perk has nothing to claim
        await _context.SaveChangesAsync();

        await LogAsync(wasNew ? "AddRewardPerk" : "EditRewardPerk", title);
        TempData["StatusMessage"] = wasNew ? "Perk added." : "Perk saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeletePerkAsync(int id)
    {
        var perk = await _context.RewardPerks.FindAsync(id);
        if (perk != null)
        {
            _context.RewardPerks.Remove(perk);
            await _context.SaveChangesAsync();
            await LogAsync("DeleteRewardPerk", perk.Title);
            TempData["StatusMessage"] = "Perk deleted. Earlier claims keep their record.";
        }
        return RedirectToPage();
    }

    // ------------------------------------------------------------------ claims
    public async Task<IActionResult> OnPostResolveClaimAsync(int claimId, string decision, string? note)
    {
        var acting = await _userManager.GetUserAsync(User);
        if (acting == null) return Forbid();

        var status = string.Equals(decision, "fulfil", StringComparison.OrdinalIgnoreCase) ? RewardClaimStatus.Fulfilled
                   : string.Equals(decision, "decline", StringComparison.OrdinalIgnoreCase) ? RewardClaimStatus.Declined
                   : (RewardClaimStatus?)null;
        if (status == null) return Fail("Choose Fulfil or Decline.", "claims");
        if (status == RewardClaimStatus.Declined && string.IsNullOrWhiteSpace(note))
            return Fail("Say why the claim is declined. The guest will read it.", "claims");

        var (ok, message) = await _perks.ResolveClaimAsync(claimId, status.Value, acting.Id, note);
        if (ok) await LogAsync(status == RewardClaimStatus.Fulfilled ? "FulfilRewardClaim" : "DeclineRewardClaim", $"Claim #{claimId}");
        TempData[ok ? "StatusMessage" : "ErrorMessage"] = message;
        return RedirectToPage(new { tab = "claims" });
    }

    // ------------------------------------------------------------------ ranking and tier messages
    /// Saves the cut-offs and wording of every rank band, and the three tier messages, in one go.
    /// Nothing is saved unless all of it is valid, so a half-edited form never leaves the guest page in a mixed state.
    public async Task<IActionResult> OnPostSaveRankingAsync(int[] bandId, int[] cut, string[] bandMessage, int[] tierId, string[] passed, string[] current, string[] locked)
    {
        const string tab = "ranking";
        var bands = await _context.RankBands.OrderBy(b => b.UpToPercent).ThenBy(b => b.SortOrder).ToListAsync();
        var tiers = await _context.RewardTiers.ToListAsync();

        if (bandId.Length != bands.Count || cut.Length != bandId.Length || bandMessage.Length != bandId.Length
            || tierId.Length != tiers.Count || passed.Length != tierId.Length || current.Length != tierId.Length || locked.Length != tierId.Length
            || bandId.Distinct().Count() != bandId.Length || tierId.Distinct().Count() != tierId.Length)
            return Fail("The bands or tiers changed while you were editing. Reload the page and try again.", tab);

        var bandById = bands.ToDictionary(b => b.Id);
        var tierById = tiers.ToDictionary(t => t.Id);
        if (bandId.Any(i => !bandById.ContainsKey(i)) || tierId.Any(i => !tierById.ContainsKey(i)))
            return Fail("The bands or tiers changed while you were editing. Reload the page and try again.", tab);

        var newBands = new List<(RankBand Band, int Cut, string Message)>();
        var previous = 0;
        for (var i = 0; i < bandId.Length; i++)
        {
            var band = bandById[bandId[i]];
            var isLast = i == bandId.Length - 1;
            var c = isLast ? 100 : cut[i];
            var label = isLast ? "Everyone else" : $"Top {c}%";
            if (!isLast && (c < 1 || c > 99)) return Fail($"A cut-off must be a whole number from 1 to 99 (the band after {(i == 0 ? "the start" : $"Top {previous}%")}).", tab);
            if (c <= previous) return Fail($"Cut-offs must rise from one band to the next. {label} needs a number above {previous}.", tab);
            var message = (bandMessage[i] ?? string.Empty).Trim();
            if (message.Length == 0) return Fail($"Write a message for {label}.", tab);
            if (message.Length > 240) return Fail($"Keep the message for {label} under 240 characters.", tab);
            if (!RankMessageRules.IsKind(message, out var word)) return Fail($"The message for {label} uses the word \"{word}\". Guests must never be told they rank low.", tab);
            newBands.Add((band, c, message));
            previous = c;
        }

        var newTiers = new List<(RewardTier Tier, string? Passed, string? Current, string? Locked)>();
        for (var i = 0; i < tierId.Length; i++)
        {
            var tier = tierById[tierId[i]];
            var texts = new[] { (passed[i], "passed"), (current[i], "current"), (locked[i], "locked") };
            var clean = new string?[3];
            for (var k = 0; k < texts.Length; k++)
            {
                var (raw, what) = texts[k];
                var text = (raw ?? string.Empty).Trim();
                if (text.Length > 200) return Fail($"Keep the {what} message for {tier.Name} under 200 characters.", tab);
                if (!RankMessageRules.IsKind(text, out var word)) return Fail($"The {what} message for {tier.Name} uses the word \"{word}\". Keep it kind.", tab);
                clean[k] = text.Length == 0 ? null : text;
            }
            newTiers.Add((tier, clean[0], clean[1], clean[2]));
        }

        for (var i = 0; i < newBands.Count; i++)
        {
            var (band, c, message) = newBands[i];
            band.UpToPercent = c;
            band.Message = message;
            band.SortOrder = i + 1;
        }
        foreach (var (tier, p, cur, lk) in newTiers)
        {
            tier.MessagePassed = p;
            tier.MessageCurrent = cur;
            tier.MessageLocked = lk;
        }
        await _context.SaveChangesAsync();
        _rewards.InvalidateTierCache();

        await LogAsync("EditRankMessages", $"{newBands.Count} bands, {newTiers.Count} tiers");
        TempData["StatusMessage"] = "Messages saved. Guests see them straight away.";
        return RedirectToPage(new { tab });
    }

    /// Puts the seven standard bands (cut-offs and wording) back. Tier messages are left as they are.
    public async Task<IActionResult> OnPostRestoreDefaultsAsync()
    {
        _context.RankBands.RemoveRange(await _context.RankBands.ToListAsync());
        _context.RankBands.AddRange(RankBandDefaults.Create());
        await _context.SaveChangesAsync();
        await LogAsync("RestoreRankBands", "Standard bands restored");
        TempData["StatusMessage"] = "The standard bands are back.";
        return RedirectToPage(new { tab = "ranking" });
    }

    private IActionResult Fail(string message, string? tab = null)
    {
        TempData["ErrorMessage"] = message;
        return tab == null ? RedirectToPage() : RedirectToPage(new { tab });
    }

    private async Task LogAsync(string action, string details)
    {
        var acting = await _userManager.GetUserAsync(User);
        if (acting != null) await _audit.LogAsync(acting.Id, acting.FullName, action, details);
    }
}
