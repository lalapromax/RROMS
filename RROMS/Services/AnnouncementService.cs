using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;

namespace RROMS.Services;

/// Who an announcement is for, as posted by the admin/staff form. Tick several boxes to combine audiences.
public record AudienceInput(bool Public, bool AllCustomers, bool AllStaff, List<int> TierIds, List<string> UserIds);

/// A person the audience picker can show (customer or staff member).
public record PersonOption(string Id, string Name, string Email, string Role, string? TierName);

public interface IAnnouncementService
{
    /// Checks the audience against what the author's role may target, then writes it onto the announcement.
    /// Returns an error message, or null when the audience is valid. Staff may only reach all customers or whole tiers.
    Task<string?> ApplyAudienceAsync(Announcement announcement, AudienceInput audience, bool authorIsAdmin);

    /// The user ids this announcement is addressed to right now (customers, staff and named people combined).
    Task<List<string>> ResolveRecipientIdsAsync(Announcement announcement);

    /// Creates in-app notifications for everyone the announcement is addressed to. Returns how many were sent.
    Task<int> NotifyAudienceAsync(Announcement announcement);

    /// Announcements this particular person is allowed to see, newest first. userId null = a visitor who is not signed in.
    Task<List<Announcement>> GetVisibleToAsync(string? userId, bool isCustomer, bool isStaff, int? take = null);

    /// Plain-English audience summary for lists ("Gold and Silver customers + 2 named people").
    string DescribeAudience(Announcement announcement, IReadOnlyList<TierInfo> tiers);

    /// Type-ahead search for the picker. Staff authors never get staff accounts or other roles back.
    Task<List<PersonOption>> SearchPeopleAsync(string? query, int? tierId, bool includeStaff, int take = 20);
}

public class AnnouncementService : IAnnouncementService
{
    private const int MaxNamedPeople = 200;

    private readonly ApplicationDbContext _context;
    private readonly IRewardService _rewards;
    private readonly INotificationService _notifications;

    public AnnouncementService(ApplicationDbContext context, IRewardService rewards, INotificationService notifications)
    {
        _context = context;
        _rewards = rewards;
        _notifications = notifications;
    }

    public async Task<string?> ApplyAudienceAsync(Announcement announcement, AudienceInput audience, bool authorIsAdmin)
    {
        var tierIds = audience.TierIds.Distinct().ToList();
        var userIds = audience.UserIds.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList();

        if (!authorIsAdmin)
        {
            // Staff may announce to all customers or to whole tiers. Never to the public, other staff, or named people.
            if (audience.Public || audience.AllStaff || userIds.Count > 0)
                return "Staff can announce to all customers or to customer tiers only.";
        }

        if (audience.Public)
        {
            // "Public" means nobody is singled out, so any other box ticked alongside it would be silently ignored.
            tierIds.Clear();
            userIds.Clear();
            audience = audience with { AllCustomers = false, AllStaff = false };
        }
        else if (!audience.AllCustomers && !audience.AllStaff && tierIds.Count == 0 && userIds.Count == 0)
        {
            return authorIsAdmin
                ? "Choose who this is for: everyone, all customers, a tier, staff, or specific people."
                : "Choose who this is for: all customers, or one or more tiers.";
        }

        if (userIds.Count > MaxNamedPeople)
            return $"You can name up to {MaxNamedPeople} people. For bigger groups use a tier or 'all customers'.";

        // Customers' tier boxes are redundant once "all customers" is ticked, so drop them to keep the record tidy.
        if (audience.AllCustomers) tierIds.Clear();

        if (tierIds.Count > 0)
        {
            var known = await _context.RewardTiers.Where(t => tierIds.Contains(t.Id)).Select(t => t.Id).ToListAsync();
            if (known.Count != tierIds.Count)
                return "One of the selected tiers no longer exists. Reload the page and try again.";
        }

        var people = new List<string>();
        if (userIds.Count > 0)
        {
            // Only real, active customer or staff accounts can be named - never admins, never made-up ids.
            var valid = await (
                from u in _context.Users
                join ur in _context.UserRoles on u.Id equals ur.UserId
                join r in _context.Roles on ur.RoleId equals r.Id
                where userIds.Contains(u.Id) && u.IsActive && (r.Name == Roles.Customer || r.Name == Roles.Staff)
                select u.Id).Distinct().ToListAsync();
            if (valid.Count != userIds.Count)
                return "One of the selected people can't receive announcements (account not found or inactive).";
            people = valid;
        }

        announcement.ToAllCustomers = audience.AllCustomers;
        announcement.ToAllStaff = audience.AllStaff;
        announcement.ToTierIds = tierIds.Count == 0 ? null : string.Join(",", tierIds.OrderBy(i => i));

        // Bring the named people in line with the new list: drop the ones removed, add only the new ones.
        var existing = announcement.Id != 0
            ? await _context.AnnouncementRecipients.Where(r => r.AnnouncementId == announcement.Id).ToListAsync()
            : new List<AnnouncementRecipient>();
        foreach (var gone in existing.Where(r => !people.Contains(r.UserId)))
            _context.AnnouncementRecipients.Remove(gone);
        foreach (var userId in people.Where(id => existing.All(r => r.UserId != id)))
            announcement.Recipients.Add(new AnnouncementRecipient { UserId = userId });

        return null;
    }

    public async Task<List<string>> ResolveRecipientIdsAsync(Announcement announcement)
    {
        var ids = new HashSet<string>();

        List<string>? customerIds = null;
        if (announcement.ToAllCustomers || announcement.IsPublic || !string.IsNullOrWhiteSpace(announcement.ToTierIds))
            customerIds = await UserIdsInRoleAsync(Roles.Customer);

        if (customerIds != null && (announcement.ToAllCustomers || announcement.IsPublic))
        {
            foreach (var id in customerIds) ids.Add(id);
        }
        else if (customerIds != null)
        {
            var wanted = announcement.GetTierIds();
            var tiers = await _rewards.GetTiersAsync();
            var totals = await _context.RewardLedger
                .GroupBy(r => r.CustomerId)
                .Select(g => new { CustomerId = g.Key, Points = g.Sum(x => x.Points) })
                .ToDictionaryAsync(x => x.CustomerId, x => x.Points);

            foreach (var id in customerIds)
            {
                totals.TryGetValue(id, out var points);
                var tier = tiers.Where(t => points >= t.MinPoints).OrderByDescending(t => t.MinPoints).First();
                if (wanted.Contains(tier.Id)) ids.Add(id);
            }
        }

        if (announcement.ToAllStaff)
            foreach (var id in await UserIdsInRoleAsync(Roles.Staff)) ids.Add(id);

        var named = await _context.AnnouncementRecipients
            .Where(r => r.AnnouncementId == announcement.Id)
            .Select(r => r.UserId)
            .ToListAsync();
        foreach (var id in named) ids.Add(id);

        return ids.ToList();
    }

    public async Task<int> NotifyAudienceAsync(Announcement announcement)
    {
        var recipients = await ResolveRecipientIdsAsync(announcement);
        return await _notifications.NotifyUsersAsync(recipients, announcement.Title, announcement.Content, NotificationType.Announcement, "/Notifications/Index#a" + announcement.Id);
    }

    public async Task<List<Announcement>> GetVisibleToAsync(string? userId, bool isCustomer, bool isStaff, int? take = null)
    {
        var now = DateTime.UtcNow;
        var live = _context.Announcements.AsNoTracking()
            .Where(a => a.IsActive && (a.PublishAt == null || a.PublishAt <= now) && (a.ExpiresAt == null || a.ExpiresAt >= now));

        IQueryable<Announcement> query;
        if (userId == null)
        {
            query = live.Where(a => !a.ToAllCustomers && !a.ToAllStaff && (a.ToTierIds == null || a.ToTierIds == "") && !a.Recipients.Any());
        }
        else
        {
            string tierToken = ",-1,";
            if (isCustomer)
            {
                var points = await _rewards.GetPointsAsync(userId);
                var tier = await _rewards.GetTierForPointsAsync(points);
                tierToken = $",{tier.Id},";
            }

            query = live.Where(a =>
                (!a.ToAllCustomers && !a.ToAllStaff && (a.ToTierIds == null || a.ToTierIds == "") && !a.Recipients.Any())
                || (isCustomer && a.ToAllCustomers)
                || (isStaff && a.ToAllStaff)
                || (isCustomer && a.ToTierIds != null && ("," + a.ToTierIds + ",").Contains(tierToken))
                || a.Recipients.Any(r => r.UserId == userId));
        }

        query = query.OrderByDescending(a => a.IsPinned).ThenByDescending(a => a.CreatedAt);
        if (take.HasValue) query = query.Take(take.Value);
        return await query.ToListAsync();
    }

    public string DescribeAudience(Announcement a, IReadOnlyList<TierInfo> tiers)
    {
        if (a.IsPublic) return "Public - everyone, including visitors";

        var parts = new List<string>();
        if (a.ToAllCustomers) parts.Add("All customers");
        var tierNames = a.GetTierIds().Select(id => tiers.FirstOrDefault(t => t.Id == id)?.Name).Where(n => n != null).ToList();
        if (tierNames.Count > 0) parts.Add($"{string.Join(" + ", tierNames)} customers");
        if (a.ToAllStaff) parts.Add("All staff");
        if (a.Recipients.Count > 0) parts.Add($"{a.Recipients.Count} named {(a.Recipients.Count == 1 ? "person" : "people")}");
        return parts.Count == 0 ? "No audience" : string.Join(" + ", parts);
    }

    public async Task<List<PersonOption>> SearchPeopleAsync(string? query, int? tierId, bool includeStaff, int take = 20)
    {
        query = query?.Trim();
        var roleNames = includeStaff ? new[] { Roles.Customer, Roles.Staff } : new[] { Roles.Customer };

        var candidatesQuery =
            from u in _context.Users
            join ur in _context.UserRoles on u.Id equals ur.UserId
            join r in _context.Roles on ur.RoleId equals r.Id
            where u.IsActive && roleNames.Contains(r.Name!)
            select new { u.Id, u.FullName, u.Email, Role = r.Name! };

        if (!string.IsNullOrEmpty(query))
            candidatesQuery = candidatesQuery.Where(c => c.FullName.Contains(query) || (c.Email != null && c.Email.Contains(query)));

        var candidates = await candidatesQuery.OrderBy(c => c.FullName).Take(tierId.HasValue ? 400 : take).ToListAsync();

        var tiers = await _rewards.GetTiersAsync();
        var ids = candidates.Where(c => c.Role == Roles.Customer).Select(c => c.Id).ToList();
        var totals = await _context.RewardLedger
            .Where(r => ids.Contains(r.CustomerId))
            .GroupBy(r => r.CustomerId)
            .Select(g => new { CustomerId = g.Key, Points = g.Sum(x => x.Points) })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Points);

        var result = new List<PersonOption>();
        foreach (var c in candidates)
        {
            string? tierName = null;
            if (c.Role == Roles.Customer)
            {
                totals.TryGetValue(c.Id, out var points);
                var tier = tiers.Where(t => points >= t.MinPoints).OrderByDescending(t => t.MinPoints).First();
                if (tierId.HasValue && tier.Id != tierId.Value) continue;
                tierName = tier.Name;
            }
            else if (tierId.HasValue)
            {
                continue; // a tier filter means customers only
            }
            result.Add(new PersonOption(c.Id, c.FullName, c.Email ?? string.Empty, c.Role, tierName));
            if (result.Count >= take) break;
        }
        return result;
    }

    private async Task<List<string>> UserIdsInRoleAsync(string roleName)
    {
        return await (
            from u in _context.Users
            join ur in _context.UserRoles on u.Id equals ur.UserId
            join r in _context.Roles on ur.RoleId equals r.Id
            where r.Name == roleName && u.IsActive
            select u.Id).Distinct().ToListAsync();
    }
}
