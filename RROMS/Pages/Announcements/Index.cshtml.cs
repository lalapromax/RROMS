using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services;
using RROMS.ViewModels;

namespace RROMS.Pages.Announcements;

/// 
/// News from the restaurant for visitors who are not signed in: only the public notices. Anyone signed in goes to Notices,
/// which adds what was sent to them (all guests, their tier, or them by name) and their own booking and order updates.
/// The filtering happens in IAnnouncementService on the server, never in the page.
/// 
[AllowAnonymous]
public class IndexModel : PageModel
{
    private readonly IAnnouncementService _announcements;

    public IndexModel(IAnnouncementService announcements)
    {
        _announcements = announcements;
    }

    public Announcement? Pinned { get; private set; }
    public List<NoticeRow> Rows { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToPage("/Notifications/Index");

        var all = await _announcements.GetVisibleToAsync(null, false, false, 40);
        Pinned = all.FirstOrDefault(a => a.IsPinned);
        Rows = all.Where(a => a != Pinned)
            .Select(a => new NoticeRow("a" + a.Id, "announcement", "bell", a.Title, a.Content, a.CreatedAt.ToLocalTime(), false, null, null))
            .ToList();
        return Page();
    }
}
