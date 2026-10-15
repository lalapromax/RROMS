using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RROMS.Data;
using RROMS.Models;

namespace RROMS.Services;

/// A reward tier as the rest of the app sees it (read from the RewardTiers table, cached briefly).
public record TierInfo(int Id, string Name, int MinPoints, string Blurb, string ColorHex,
    string? MessagePassed = null, string? MessageCurrent = null, string? MessageLocked = null);

public record RewardStanding(
    int Points,
    TierInfo Tier,
    TierInfo? NextTier,
    int PointsToNext,
    int PercentToNext,
    int? Rank,
    int RankedCustomers);

public record LeaderboardRow(int Rank, string DisplayName, string TierName, int Points, bool IsYou);

/// 
/// Where a guest stands among everyone who has earned points, worded so it never discourages. Numbers (rank, percentage) are only
/// shown in the top half (<see cref="ShowNumbers"/>); below that the guest sees a kind message and a next goal.
/// 
public record RankStanding(
    bool Ranked,
    int Points,
    int? Rank,
    int Guests,
    int PercentAhead,
    int BandIndex,                       // 0-based position on the scale; -1 when not ranked yet
    IReadOnlyList<string> BandLabels,    // "Top 1%" ... "Everyone", for the scale under the card
    string BandLabel,
    string Headline,                     // "Top 5%", "Ahead of 62%", "Glad you are here", "Thank you", "Not ranked yet"
    bool HeadlineIsBand,                 // the top bands show the band itself as the big figure
    string Message,
    bool ShowNumbers,
    string? Goal,
    int TopPoints,
    int TenPercentPoints,
    int TypicalPoints,
    bool ShowCompare);

public interface IRewardService
{
    /// All tiers, lowest first. Always has at least one entry (a built-in "Member" tier is used if the table is empty).
    Task<IReadOnlyList<TierInfo>> GetTiersAsync();

    /// Call after an administrator changes tiers so the next page load sees the change immediately.
    void InvalidateTierCache();

    Task<int> GetPointsAsync(string customerId);
    Task<TierInfo> GetTierForPointsAsync(int points);

    int PointsPerCurrencyUnit { get; }

    /// 
    /// Awards points for one order, but only when it genuinely qualifies: the order is
    /// Completed AND its payment is Paid. Safe to call repeatedly (every place an order or
    /// payment changes calls it) - the unique OrderId index guarantees a single award.
    /// Returns the points awarded by THIS call (0 when ineligible or already awarded).
    /// 
    Task<int> TryAwardForOrderAsync(int orderId);

    Task<RewardStanding> GetStandingAsync(string customerId);
    Task<List<LeaderboardRow>> GetLeaderboardAsync(string? viewerId, int take = 10);

    /// The guest's place among everyone with points: band, percentage, kind message and next goal.
    Task<RankStanding> GetRankAsync(string customerId);
    Task<List<RewardLedgerEntry>> GetHistoryAsync(string customerId, int take = 15);
}

/// 
/// A deliberately small loyalty scheme (brief 6.F): spend earns points, lifetime points decide
/// a Bronze/Silver/Gold tier, and a masked-name leaderboard shows who is ahead. All maths
/// happens here on the server from real, paid, completed orders - the browser never reports
/// points. Tiers are rows an administrator edits (Admin > Rewards); the perks each tier unlocks and the
/// claim queue live in <see cref="IRewardPerkService"/>.
/// 
public class RewardService : IRewardService
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly IConfiguration _config;
    private readonly ILogger<RewardService> _logger;
    private readonly IMemoryCache _cache;

    private const string TierCacheKey = "reward-tiers";

    public RewardService(ApplicationDbContext context, INotificationService notificationService, IConfiguration config, ILogger<RewardService> logger, IMemoryCache cache)
    {
        _context = context;
        _notificationService = notificationService;
        _config = config;
        _logger = logger;
        _cache = cache;
    }

    public int PointsPerCurrencyUnit => Math.Max(1, _config.GetValue<int?>("RewardSettings:PointsPerCurrencyUnit") ?? 1);

    public async Task<IReadOnlyList<TierInfo>> GetTiersAsync()
    {
        if (_cache.TryGetValue(TierCacheKey, out IReadOnlyList<TierInfo>? cached) && cached != null)
            return cached;

        var rows = await _context.RewardTiers.AsNoTracking().OrderBy(t => t.MinPoints).ToListAsync();
        IReadOnlyList<TierInfo> tiers = rows.Count == 0
            ? new List<TierInfo> { new(0, "Member", 0, "Every diner starts here.", "#6C7F8E") }
            : rows.Select(t => new TierInfo(t.Id, t.Name, t.MinPoints, t.Blurb ?? string.Empty, t.ColorHex, t.MessagePassed, t.MessageCurrent, t.MessageLocked)).ToList();

        _cache.Set(TierCacheKey, tiers, TimeSpan.FromMinutes(5));
        return tiers;
    }

    public void InvalidateTierCache() => _cache.Remove(TierCacheKey);

    public async Task<TierInfo> GetTierForPointsAsync(int points)
    {
        var tiers = await GetTiersAsync();
        return TierFor(tiers, points);
    }

    public async Task<int> TryAwardForOrderAsync(int orderId)
    {
        var order = await _context.Orders
            .Include(o => o.Payment)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null || order.Status != OrderStatus.Completed || order.Payment?.Status != PaymentStatus.Paid)
            return 0;

        var points = (int)Math.Floor(order.TotalAmount) * PointsPerCurrencyUnit;
        if (points <= 0)
            return 0;

        if (await _context.RewardLedger.AnyAsync(r => r.OrderId == orderId))
            return 0; // already awarded - the common repeat-call path

        var before = await GetPointsAsync(order.CustomerId);

        _context.RewardLedger.Add(new RewardLedgerEntry
        {
            CustomerId = order.CustomerId,
            OrderId = order.Id,
            Points = points,
            Reason = $"Order #{order.Id} completed and paid",
            AwardedAt = DateTime.UtcNow
        });

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // Two requests raced to award the same order and the unique index rejected the loser.
            _logger.LogInformation(ex, "Reward for order {OrderId} was already recorded by another request.", orderId);
            _context.ChangeTracker.Clear();
            return 0;
        }

        var after = before + points;
        var tiersNow = await GetTiersAsync();
        var oldTier = TierFor(tiersNow, before);
        var newTier = TierFor(tiersNow, after);

        await _notificationService.NotifyAsync(
            order.CustomerId,
            "Reward points earned",
            $"You earned {points} points for order #{order.Id}. You now have {after} points.",
            NotificationType.Reward,
            "/Rewards/Index",
            sendEmail: false);

        if (newTier.MinPoints > oldTier.MinPoints)
        {
            await _notificationService.NotifyAsync(
                order.CustomerId,
                $"You reached {newTier.Name}!",
                $"Thanks for dining with us - you are now a {newTier.Name} member. New perks are waiting on your Rewards page.",
                NotificationType.Reward,
                "/Rewards/Index");
        }

        return points;
    }

    public async Task<RewardStanding> GetStandingAsync(string customerId)
    {
        var points = await GetPointsAsync(customerId);
        var tiers = await GetTiersAsync();
        var tier = TierFor(tiers, points);
        var index = tiers.ToList().FindIndex(t => t.Id == tier.Id);
        var next = index + 1 < tiers.Count ? tiers[index + 1] : null;

        int toNext = 0, percent = 100;
        if (next != null)
        {
            var span = Math.Max(1, next.MinPoints - tier.MinPoints);
            toNext = Math.Max(0, next.MinPoints - points);
            percent = Math.Clamp((int)((points - tier.MinPoints) * 100L / span), 0, 100);
        }

        int? rank = null;
        var ranked = await _context.RewardLedger.Select(r => r.CustomerId).Distinct().CountAsync();
        if (points > 0)
        {
            var ahead = await _context.RewardLedger
                .GroupBy(r => r.CustomerId)
                .Select(g => g.Sum(x => x.Points))
                .Where(total => total > points)
                .CountAsync();
            rank = ahead + 1;
        }

        return new RewardStanding(points, tier, next, toNext, percent, rank, ranked);
    }

    public async Task<List<LeaderboardRow>> GetLeaderboardAsync(string? viewerId, int take = 10)
    {
        var all = await _context.RewardLedger
            .GroupBy(r => r.CustomerId)
            .Select(g => new { CustomerId = g.Key, Points = g.Sum(x => x.Points) })
            .Where(x => x.Points > 0)
            .ToListAsync();
        var ordered = all.OrderByDescending(x => x.Points).ThenBy(x => x.CustomerId).ToList();

        // Guests who chose to stay off the board (and closed accounts) are skipped, but everyone else keeps their true place.
        var look = ordered.Take(Math.Max(take * 5, 25)).Select(t => t.CustomerId).ToList();
        var people = await _context.Users
            .Where(u => look.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.ShowOnLeaderboard, u.IsActive })
            .ToDictionaryAsync(u => u.Id);

        var tiers = await GetTiersAsync();
        var rows = new List<LeaderboardRow>();
        for (var i = 0; i < ordered.Count && rows.Count < take && i < look.Count; i++)
        {
            var t = ordered[i];
            if (!people.TryGetValue(t.CustomerId, out var person) || !person.IsActive) continue;
            var isYou = viewerId != null && t.CustomerId == viewerId;
            if (!person.ShowOnLeaderboard) continue;
            var rank = ordered.Count(x => x.Points > t.Points) + 1; // equal points share a place
            rows.Add(new LeaderboardRow(rank, MaskName(person.FullName), TierFor(tiers, t.Points).Name, t.Points, isYou));
        }
        return rows;
    }

    public async Task<RankStanding> GetRankAsync(string customerId)
    {
        var totals = await _context.RewardLedger
            .GroupBy(r => r.CustomerId)
            .Select(g => new { CustomerId = g.Key, Points = g.Sum(x => x.Points) })
            .Where(x => x.Points > 0)
            .ToListAsync();

        var bands = await _context.RankBands.AsNoTracking().OrderBy(b => b.UpToPercent).ToListAsync();
        if (bands.Count == 0)
            bands.Add(new RankBand { UpToPercent = 100, Message = "Thanks for being a loyal guest of ours. We look forward to your next visit.", SortOrder = 1 });
        var labels = bands.Select(b => b.Label).ToList();

        var mine = totals.FirstOrDefault(t => t.CustomerId == customerId)?.Points ?? 0;
        if (mine <= 0)
        {
            return new RankStanding(false, 0, null, totals.Count, 0, -1, labels, "No orders yet", "Not ranked yet", false,
                "Your first paid order starts your count. We would love to see you.", false,
                "Order from the menu and pay to join the board.", 0, 0, 0, false);
        }

        var sorted = totals.Select(t => t.Points).OrderByDescending(p => p).ToList();
        var guests = sorted.Count;
        var rank = sorted.Count(p => p > mine) + 1;
        var percentAhead = (guests - rank) * 100 / guests;
        // The top guest is always in the first band, however few guests there are.
        var topPercent = rank == 1 ? 0.0 : rank * 100.0 / guests;
        var index = bands.FindIndex(b => topPercent <= b.UpToPercent);
        if (index < 0) index = bands.Count - 1;
        var band = bands[index];

        var showNumbers = band.UpToPercent <= 50;
        var headlineIsBand = band.UpToPercent <= 10;
        var headline = headlineIsBand ? band.Label
            : showNumbers ? $"Ahead of {percentAhead}%"
            : band.UpToPercent < 100 ? "Glad you are here" : "Thank you";

        // Next goal: only below the top-10% bands, and worded as how many more points reach the next band up.
        string? goal = null;
        if (band.UpToPercent > 10 && index > 0)
        {
            var next = bands[index - 1];
            var maxRank = Math.Max(1, (int)Math.Floor(next.UpToPercent * guests / 100.0));
            var others = sorted.ToList();
            others.Remove(mine); // me, once
            if (maxRank <= others.Count)
            {
                var needed = Math.Max(1, others[maxRank - 1] - mine);
                var where = next.UpToPercent switch
                {
                    25 => "puts you in the top quarter",
                    50 => "puts you in the top half",
                    > 50 => "moves you up a band",
                    _ => $"puts you in the top {next.UpToPercent}%"
                };
                goal = $"{needed:N0} more {(needed == 1 ? "point" : "points")} {where}.";
            }
        }

        var top = sorted[0];
        var tenIndex = Math.Max(0, (int)Math.Floor(guests * 0.10) - 1);
        var ten = sorted[Math.Min(tenIndex, guests - 1)];
        var typical = sorted[guests / 2];
        var showCompare = showNumbers && guests >= 5;

        return new RankStanding(true, mine, rank, guests, percentAhead, index, labels, band.Label, headline, headlineIsBand,
            band.Message, showNumbers, goal, top, ten, typical, showCompare);
    }

    public async Task<List<RewardLedgerEntry>> GetHistoryAsync(string customerId, int take = 15)
    {
        return await _context.RewardLedger
            .Where(r => r.CustomerId == customerId)
            .OrderByDescending(r => r.AwardedAt)
            .Take(take)
            .ToListAsync();
    }

    public async Task<int> GetPointsAsync(string customerId) =>
        await _context.RewardLedger.Where(r => r.CustomerId == customerId).SumAsync(r => (int?)r.Points) ?? 0;

    private static TierInfo TierFor(IReadOnlyList<TierInfo> tiers, int points) =>
        tiers.Where(t => points >= t.MinPoints).OrderByDescending(t => t.MinPoints).FirstOrDefault() ?? tiers[0];

    /// "Aisha Karim" -> "Aisha K." so the public leaderboard never shows a full name.
    internal static string MaskName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "Guest";
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1 ? parts[0] : $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.";
    }
}
