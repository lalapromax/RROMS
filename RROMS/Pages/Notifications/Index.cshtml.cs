using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;
using RROMS.ViewModels;

namespace RROMS.Pages.Notifications;

/// 
/// Notices: news from the restaurant and updates about the signed-in person's own bookings, orders and rewards, in one list.
/// Shared by guests, staff and administrators; each sees only their own notices, and announcements are filtered on the
/// server by <see cref="IAnnouncementService"/>.
/// 
public class IndexModel : PageModel
{
    private readonly INotificationService _notifications;
    private readonly IAnnouncementService _announcements;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;

    public IndexModel(INotificationService notifications, IAnnouncementService announcements,
        UserManager<ApplicationUser> userManager, ApplicationDbContext context)
    {
        _notifications = notifications;
        _announcements = announcements;
        _userManager = userManager;
        _context = context;
    }

    /// all, announcements, orders or rewards.
    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "all";

    public bool IsCustomer { get; private set; }
    public List<NoticeRow> Items { get; private set; } = new();
    public Announcement? Pinned { get; private set; }
    public Dictionary<string, int> Counts { get; private set; } = new();
    public NoticePrefsVM Prefs { get; private set; } = new(true, true, false, true);
    public int UnreadCount { get; private set; }

    public async Task OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        var userId = user!.Id;
        IsCustomer = User.IsInRole(Roles.Customer);
        Prefs = new NoticePrefsVM(user.NoticeAnnouncements, user.NoticeRewards, user.NoticeOffers, user.NoticeByEmail);

        var mine = await _notifications.GetForUserAsync(userId, 60);
        UnreadCount = mine.Count(n => !n.IsRead);

        var visible = await _announcements.GetVisibleToAsync(userId, IsCustomer, User.IsInRole(Roles.Staff), 40);

        // An announcement is "new" while the notice that told this person about it is still unread.
        var unreadAnnouncementIds = mine.Where(n => !n.IsRead && n.Type == NotificationType.Announcement && n.LinkUrl != null && n.LinkUrl.Contains("#a"))
            .Select(n => n.LinkUrl![(n.LinkUrl!.IndexOf("#a", StringComparison.Ordinal) + 2)..]).ToHashSet();

        Pinned = visible.FirstOrDefault(a => a.IsPinned);
        var items = new List<NoticeRow>();
        foreach (var a in visible.Where(a => a != Pinned))
            items.Add(new NoticeRow("a" + a.Id, "announcement", "bell", a.Title, a.Content, a.CreatedAt.ToLocalTime(),
                unreadAnnouncementIds.Contains(a.Id.ToString()), null, null));

        foreach (var n in mine.Where(n => n.Type != NotificationType.Announcement))
        {
            var (kind, icon) = n.Type switch
            {
                NotificationType.Reservation => ("booking", "cal"),
                NotificationType.Order or NotificationType.Payment => ("order", "receipt"),
                NotificationType.Reward => ("reward", "crown"),
                NotificationType.Promotion => ("offer", "gift"),
                _ => ("system", "bell")
            };
            items.Add(new NoticeRow("n" + n.Id, kind, icon, n.Title, n.Message, n.CreatedAt.ToLocalTime(), !n.IsRead, n.Id, SafeLocal(n.LinkUrl)));
        }

        items = items.OrderByDescending(i => i.When).ToList();
        Counts = new Dictionary<string, int>
        {
            ["all"] = items.Count + (Pinned != null ? 1 : 0),
            ["announcements"] = items.Count(i => i.Kind == "announcement") + (Pinned != null ? 1 : 0),
            ["orders"] = items.Count(i => i.Kind == "order"),
            ["rewards"] = items.Count(i => i.Kind == "reward")
        };

        Tab = Tab?.ToLowerInvariant() is "announcements" or "orders" or "rewards" ? Tab.ToLowerInvariant() : "all";
        if (Tab == "rewards" && !IsCustomer) Tab = "all";
        Items = Tab switch
        {
            "announcements" => items.Where(i => i.Kind == "announcement").ToList(),
            "orders" => items.Where(i => i.Kind == "order").ToList(),
            "rewards" => items.Where(i => i.Kind == "reward").ToList(),
            _ => items
        };
        if (Tab is "orders" or "rewards") Pinned = null;
    }

    /// Opening a notice that links somewhere marks it read on the way.
    public async Task<IActionResult> OnGetOpenAsync(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var n = await _context.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (n == null) return RedirectToPage();
        n.IsRead = true;
        await _context.SaveChangesAsync();
        var link = SafeLocal(n.LinkUrl);
        return link != null ? LocalRedirect(link) : RedirectToPage();
    }

    public async Task<IActionResult> OnPostMarkAllReadAsync()
    {
        await _notifications.MarkAllReadAsync(_userManager.GetUserId(User)!);
        return RedirectToPage(new { tab = Tab == "all" ? null : Tab });
    }

    public async Task<IActionResult> OnPostPrefsAsync(bool announcements, bool rewards, bool offers, bool email)
    {
        await _notifications.SavePreferencesAsync(_userManager.GetUserId(User)!, announcements, rewards, offers, email);
        TempData["StatusMessage"] = "Saved. We will reach you the way you chose.";
        return RedirectToPage(new { tab = Tab == "all" ? null : Tab });
    }

    /// Only links inside this site are followed; anything else is ignored.
    private static string? SafeLocal(string? url) =>
        !string.IsNullOrWhiteSpace(url) && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\") ? url : null;
}
