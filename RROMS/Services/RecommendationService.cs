using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RROMS.Data;
using RROMS.Models;

namespace RROMS.Services;

/// 
/// What the menu's "You might like" rail shows, plus an honest description of HOW it was chosen
/// (brief 6.I: a rule-based list must never be passed off as an AI model).
/// 
public record RecommendationResult(
    List<MenuItem> Items,
    bool UsedAi,
    string Method,
    string Note,
    IReadOnlyDictionary<int, string> Reasons);

public interface IRecommendationService
{
    Task<List<MenuItem>> GetRecommendationsAsync(string? customerId, int count = 6);
    Task<RecommendationResult> GetForYouAsync(string? customerId, int count = 4);
    Task<List<MenuItem>> GetPopularItemsAsync(int count = 6);
}

/// 
/// "You might like" recommendations.
///
/// Order of decisions - this order is what keeps people safe:
///   1. Dietary safety first. Every dish that fails DietaryRules.IsSuitable for the signed-in
///      customer (diet label or allergy) is removed before any ranking happens, so neither the
///      rules below nor the optional AI can ever recommend it.
///   2. Rule-based ranking (always available, no AI): how much the customer orders from each
///      category, what other guests order, and how guests rate the dish.
///   3. Optional local AI (off by default - see <see cref="OllamaRecommender"/>): re-orders a short
///      list of dishes that already passed step 1. If it is off, down, slow or answers badly, the
///      rule-based result from step 2 is used and labelled as rule-based.
/// 
public class RecommendationService : IRecommendationService
{
    private const int MinReviewsForDishRating = 3;
    private const int AiCandidatePoolSize = 12;

    private readonly ApplicationDbContext _context;
    private readonly ILocalAiRecommender _ai;
    private readonly IMemoryCache _cache;

    public RecommendationService(ApplicationDbContext context, ILocalAiRecommender ai, IMemoryCache cache)
    {
        _context = context;
        _ai = ai;
        _cache = cache;
    }

    private sealed record AiCacheEntry(List<AiPick>? Picks);

    public async Task<List<MenuItem>> GetRecommendationsAsync(string? customerId, int count = 6)
        => (await GetForYouAsync(customerId, count)).Items;

    public async Task<RecommendationResult> GetForYouAsync(string? customerId, int count = 4)
    {
        var popularity = await GetItemPopularityAsync();

        // Guests: no profile, no history - just what is popular.
        if (string.IsNullOrEmpty(customerId))
        {
            var popular = await GetPopularItemsAsync(count);
            return PopularResult(popular, "Our most-ordered dishes. Log in to get suggestions based on your own orders.");
        }

        // Only customers' dietary profiles are applied; a staff member's profile exists for
        // staff-meal planning (brief Section H) and must not shape what they see here.
        var profile = await GetCustomerDietaryProfileAsync(customerId);
        var ratings = await GetDishRatingsAsync();

        // Step 1: everything this customer can actually eat, right now.
        var available = await _context.MenuItems.Include(m => m.MenuCategory).Where(m => m.IsAvailable).ToListAsync();
        var safe = available.Where(m => DietaryRules.IsSuitable(m, profile)).ToList();

        var history = await _context.OrderItems
            .Where(oi => oi.Order!.CustomerId == customerId && oi.Order.Status != OrderStatus.Cancelled)
            .GroupBy(oi => oi.MenuItemId)
            .Select(g => new { MenuItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToListAsync();

        var maxPopularity = popularity.Count > 0 ? popularity.Values.Max() : 0;
        double PopularityScore(MenuItem m) => maxPopularity > 0 && popularity.TryGetValue(m.Id, out var q) ? (double)q / maxPopularity : 0;
        double RatingBoost(MenuItem m) => ratings.TryGetValue(m.Id, out var avg) ? (avg - 3.0) / 2.0 * 0.5 : 0;

        // No order history yet: popular dishes this customer can eat.
        if (history.Count == 0)
        {
            var starter = safe
                .OrderByDescending(m => PopularityScore(m) + RatingBoost(m))
                .ThenBy(m => m.Name)
                .Take(count)
                .ToList();
            var starterReasons = starter.ToDictionary(m => m.Id, m => ratings.ContainsKey(m.Id) && ratings[m.Id] >= 4.3 ? "Highly rated by guests" : "Popular with other guests");
            return new RecommendationResult(
                starter, false, "Popular right now",
                "Popular dishes that fit your dietary profile. Order a few things and these will start reflecting your own taste.",
                starterReasons);
        }

        // Step 2: rule-based ranking of dishes they haven't tried yet.
        var orderedIds = history.Select(h => h.MenuItemId).ToHashSet();
        var itemsById = available.ToDictionary(m => m.Id);
        var totalQty = history.Sum(h => h.Quantity);

        // Category affinity: the share of this customer's past quantity that came from each category.
        var categoryShare = new Dictionary<int, double>();
        foreach (var h in history)
        {
            // The dish may have been removed or made unavailable since; fall back to a lookup.
            var categoryId = itemsById.TryGetValue(h.MenuItemId, out var known)
                ? known.MenuCategoryId
                : await _context.MenuItems.Where(m => m.Id == h.MenuItemId).Select(m => m.MenuCategoryId).FirstOrDefaultAsync();
            categoryShare[categoryId] = categoryShare.GetValueOrDefault(categoryId) + (double)h.Quantity / totalQty;
        }

        double RuleScore(MenuItem m) => 3.0 * categoryShare.GetValueOrDefault(m.MenuCategoryId) + PopularityScore(m) + RatingBoost(m);

        var untried = safe.Where(m => !orderedIds.Contains(m.Id))
            .OrderByDescending(RuleScore)
            .ThenBy(m => m.Name)
            .ToList();

        var ruleReasons = new Dictionary<int, string>();
        foreach (var m in untried)
        {
            if (categoryShare.GetValueOrDefault(m.MenuCategoryId) >= 0.3)
                ruleReasons[m.Id] = $"You often order from {m.MenuCategory?.Name ?? "this category"}";
            else if (ratings.TryGetValue(m.Id, out var avg) && avg >= 4.3)
                ruleReasons[m.Id] = "Highly rated by guests";
            else if (PopularityScore(m) >= 0.5)
                ruleReasons[m.Id] = "Popular with other guests";
            else
                ruleReasons[m.Id] = "Something new to try";
        }

        var ruleBased = untried.Take(count).ToList();

        // Not enough new dishes to fill the rail: top up with ones they already enjoy, most-ordered first.
        if (ruleBased.Count < count)
        {
            var again = safe.Where(m => orderedIds.Contains(m.Id))
                .OrderByDescending(m => history.First(h => h.MenuItemId == m.Id).Quantity)
                .ThenBy(m => m.Name)
                .Take(count - ruleBased.Count)
                .ToList();
            foreach (var m in again) ruleReasons[m.Id] = "You've ordered this before";
            ruleBased.AddRange(again);
        }

        var ruleResult = new RecommendationResult(
            ruleBased, false, "Based on your past orders",
            "Chosen from the menu categories you order from most, what other guests order, and how guests rate each dish. Dishes that don't fit your dietary profile are left out.",
            ruleReasons);

        // Step 3: optional local AI over a short list that already passed step 1.
        if (!_ai.IsEnabled || untried.Count == 0)
            return ruleResult;

        var pool = untried.Take(AiCandidatePoolSize).ToList();
        var picks = await GetAiPicksAsync(customerId, profile, history.Select(h => (h.MenuItemId, h.Quantity)).ToList(), itemsById, pool, ratings, count);
        if (picks == null)
            return ruleResult;

        var aiItems = new List<MenuItem>();
        var aiReasons = new Dictionary<int, string>();
        foreach (var pick in picks)
        {
            var dish = pool.FirstOrDefault(p => p.Id == pick.Id);
            if (dish == null || aiItems.Any(a => a.Id == dish.Id)) continue; // belt and braces: only dishes from the safe pool
            aiItems.Add(dish);
            aiReasons[dish.Id] = pick.Reason ?? ruleReasons.GetValueOrDefault(dish.Id, "Picked for you");
        }

        // If the model returned fewer dishes than asked for, fill from the rule-based order.
        foreach (var dish in untried)
        {
            if (aiItems.Count >= count) break;
            if (aiItems.Any(a => a.Id == dish.Id)) continue;
            aiItems.Add(dish);
            aiReasons[dish.Id] = ruleReasons.GetValueOrDefault(dish.Id, "Something new to try");
        }

        return new RecommendationResult(
            aiItems.Take(count).ToList(), true, "Picked for you",
            "Ranked on this restaurant's own computer from your past orders, choosing only from dishes that fit your dietary profile. Your allergy details are never sent anywhere.",
            aiReasons);
    }

    private async Task<List<AiPick>?> GetAiPicksAsync(
        string customerId,
        DietaryProfile profile,
        List<(int MenuItemId, int Quantity)> history,
        Dictionary<int, MenuItem> itemsById,
        List<MenuItem> pool,
        Dictionary<int, double> ratings,
        int count)
    {
        // Same customer + same history + same candidates + same model = same answer, so reuse it. Failures are
        // cached briefly too, so a stopped Ollama doesn't make every page load wait for the timeout.
        var cacheKey = $"foryou-ai:{customerId}:{history.Sum(h => h.Quantity)}:{string.Join(',', pool.Select(p => p.Id))}:{_ai.ModelName}:{count}";
        if (_cache.TryGetValue(cacheKey, out AiCacheEntry? cached) && cached != null)
            return cached.Picks;

        var summary = BuildTasteSummary(profile, history, itemsById);
        var candidates = pool.Select(p => new AiCandidate(
            p.Id, p.Name, p.MenuCategory?.Name ?? "Menu", p.Description, p.Price, p.IsVegetarian, p.IsSpicy,
            ratings.TryGetValue(p.Id, out var avg) ? Math.Round(avg, 1) : null)).ToList();

        var picks = await _ai.RecommendAsync(summary, candidates, count);
        _cache.Set(cacheKey, new AiCacheEntry(picks), picks != null ? TimeSpan.FromMinutes(30) : TimeSpan.FromMinutes(2));
        return picks;
    }

    private static string BuildTasteSummary(DietaryProfile profile, List<(int MenuItemId, int Quantity)> history, Dictionary<int, MenuItem> itemsById)
    {
        var favourites = history
            .Where(h => itemsById.ContainsKey(h.MenuItemId))
            .OrderByDescending(h => h.Quantity)
            .Take(6)
            .Select(h => $"{itemsById[h.MenuItemId].Name} x{h.Quantity}");

        var lines = new List<string> { "Has ordered before: " + string.Join(", ", favourites) + "." };

        // Only a diet label from our own fixed list goes into the prompt - never free text, and never allergies.
        var labels = profile.Preferences.Where(p => DietaryOptions.Common.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
        if (labels.Count > 0)
            lines.Add("Dietary preference: " + string.Join(", ", labels) + ".");

        return string.Join(Environment.NewLine, lines);
    }

    public async Task<List<MenuItem>> GetPopularItemsAsync(int count = 6)
    {
        var popularity = await GetItemPopularityAsync();

        var available = await _context.MenuItems
            .Include(m => m.MenuCategory)
            .Where(m => m.IsAvailable)
            .ToListAsync();

        return available
            .OrderByDescending(m => popularity.TryGetValue(m.Id, out var qty) ? qty : 0)
            .ThenBy(m => m.Name)
            .Take(count)
            .ToList();
    }

    private static RecommendationResult PopularResult(List<MenuItem> items, string note) =>
        new(items, false, "Popular right now", note,
            items.ToDictionary(m => m.Id, _ => "Popular with other guests"));

    private async Task<DietaryProfile> GetCustomerDietaryProfileAsync(string customerId)
    {
        var customerRoleId = await _context.Roles.Where(r => r.Name == Roles.Customer).Select(r => r.Id).FirstOrDefaultAsync();
        var isCustomer = customerRoleId != null &&
            await _context.UserRoles.AnyAsync(ur => ur.UserId == customerId && ur.RoleId == customerRoleId);
        if (!isCustomer) return new DietaryProfile();

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == customerId);
        return DietaryRules.ProfileFor(user);
    }

    private async Task<Dictionary<int, int>> GetItemPopularityAsync()
    {
        return await _context.OrderItems
            .GroupBy(oi => oi.MenuItemId)
            .Select(g => new { MenuItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.MenuItemId, x => x.Quantity);
    }

    /// 
    /// Average overall rating of the reviews written for orders that contained each dish - an indirect
    /// signal, because reviews are per order, not per dish - and only once at least
    /// <see cref="MinReviewsForDishRating"/> reviews back it up.
    /// 
    private async Task<Dictionary<int, double>> GetDishRatingsAsync()
    {
        var rows = await _context.Feedbacks
            .Where(f => f.OrderId != null)
            .SelectMany(f => f.Order!.OrderItems.Select(oi => new { oi.MenuItemId, f.Rating }))
            .ToListAsync();

        return rows
            .GroupBy(r => r.MenuItemId)
            .Where(g => g.Count() >= MinReviewsForDishRating)
            .ToDictionary(g => g.Key, g => g.Average(r => (double)r.Rating));
    }
}
