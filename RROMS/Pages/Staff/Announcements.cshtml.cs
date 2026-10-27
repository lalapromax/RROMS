using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Presentation;
using RROMS.Services;

namespace RROMS.Pages.Staff;

/// 
/// Staff post news to guests: to all customers, or to whole tiers. Never to the public, other staff or named guests - that
/// limit is enforced in <see cref="IAnnouncementService.ApplyAudienceAsync"/> (so a hand-edited form cannot get round it), and
/// staff can only change or remove what they wrote themselves. An announcement is a draft until it is published; it can be
/// pinned to the top of guests' Notices and may carry one button that links to a page of this site.
/// 
public class AnnouncementsModel : PageModel
{
    public const int TitleMax = 150, MessageMax = 1000, ButtonTextMax = 40, ButtonUrlMax = 200;

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAnnouncementService _announcements;
    private readonly IRewardService _rewards;

    public AnnouncementsModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager,
        IAnnouncementService announcements, IRewardService rewards)
    {
        _context = context;
        _userManager = userManager;
        _announcements = announcements;
        _rewards = rewards;
    }

    [BindProperty(SupportsGet = true)] public string? Tab { get; set; }
    [BindProperty(Name = "F")] public AnnouncementInput F { get; set; } = new();

    public IReadOnlyList<TierInfo> Tiers { get; private set; } = Array.Empty<TierInfo>();
    public List<Row> Rows { get; private set; } = new();
    public Dictionary<string, int> Counts { get; private set; } = new();
    public bool OpenForm { get; private set; }

    public static readonly (string Key, string Label)[] Tabs =
    {
        ("all", "All"), ("published", "Published"), ("scheduled", "Scheduled"), ("drafts", "Drafts")
    };

    public record Row(Announcement A, string State, string Css, string Audience, string When, bool Pinned);

    public class AnnouncementInput
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public string? Content { get; set; }
        public bool AllCustomers { get; set; }
        public List<int> TierIds { get; set; } = new();
        /// "now" or "later".
        public string When { get; set; } = "now";
        public DateTime? PublishAt { get; set; }
        public bool Pinned { get; set; }
        public string? ButtonText { get; set; }
        public string? ButtonUrl { get; set; }
        /// Also send each guest a notice when this goes live.
        public bool Notify { get; set; } = true;
        /// "publish" or "draft".
        public string Intent { get; set; } = "publish";
    }

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        Tiers = await _rewards.GetTiersAsync();
        var tierNames = Tiers.ToDictionary(t => t.Id, t => t.Name);
        var me = _userManager.GetUserId(User);
        var mine = await _context.Announcements.AsNoTracking()
            .Include(a => a.Recipients)
            .Where(a => a.CreatedByAdminId == me)
            .OrderByDescending(a => a.IsPinned).ThenByDescending(a => a.PublishAt ?? a.CreatedAt)
            .Take(100)
            .ToListAsync();

        var now = DateTime.UtcNow;
        var all = mine.Select(a =>
        {
            var state = AnnouncementLabels.State(a, now);
            return new Row(a, state, AnnouncementLabels.Css(state), AnnouncementLabels.Audience(a, tierNames),
                AnnouncementLabels.When(a, now), a.IsPinned);
        }).ToList();

        Counts = new Dictionary<string, int>
        {
            ["all"] = all.Count,
            ["published"] = all.Count(r => r.State is "Published" or "Ended"),
            ["scheduled"] = all.Count(r => r.State == "Scheduled"),
            ["drafts"] = all.Count(r => r.State == "Draft")
        };
        Tab = Tabs.Any(t => t.Key == Tab) ? Tab : "all";
        Rows = Tab switch
        {
            "published" => all.Where(r => r.State is "Published" or "Ended").ToList(),
            "scheduled" => all.Where(r => r.State == "Scheduled").ToList(),
            "drafts" => all.Where(r => r.State == "Draft").ToList(),
            _ => all
        };
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var problem = Validate(out var publishAtUtc);
        if (problem != null) return await Reopen(problem);

        var publish = F.Intent == "publish";
        var now = DateTime.UtcNow;
        var me = _userManager.GetUserId(User);

        Announcement a;
        var wasLive = false;
        if (F.Id > 0)
        {
            var found = await _context.Announcements.Include(x => x.Recipients)
                .FirstOrDefaultAsync(x => x.Id == F.Id && x.CreatedByAdminId == me);
            if (found == null)
            {
                TempData["ErrorMessage"] = "You can only change announcements you posted.";
                return RedirectToPage(new { tab = Tab });
            }
            a = found;
            wasLive = a.IsActive && (a.PublishAt == null || a.PublishAt <= now);
        }
        else
        {
            a = new Announcement { CreatedAt = now, CreatedByAdminId = me, CreatedByRole = Roles.Staff };
        }

        // Staff posts carry no public / staff / named-guest options; the service refuses them anyway.
        var audienceProblem = await _announcements.ApplyAudienceAsync(a,
            new AudienceInput(false, F.AllCustomers, false, F.TierIds ?? new(), new()), authorIsAdmin: false);
        if (audienceProblem != null) return await Reopen(audienceProblem);

        a.Title = F.Title!.Trim();
        a.Content = F.Content!.Trim();
        a.IsPinned = F.Pinned;
        a.ButtonText = string.IsNullOrWhiteSpace(F.ButtonUrl) ? null : F.ButtonText!.Trim();
        a.ButtonUrl = string.IsNullOrWhiteSpace(F.ButtonUrl) ? null : F.ButtonUrl.Trim();
        a.IsActive = publish;
        a.PublishAt = publish && F.When == "later" ? publishAtUtc : null;

        var liveNow = publish && (a.PublishAt == null || a.PublishAt <= now);
        var tellNow = false;
        if (!publish || !F.Notify || wasLive) a.NotifyPending = false;
        else if (liveNow) { a.NotifyPending = false; tellNow = true; }
        else a.NotifyPending = true;

        if (a.Id == 0) _context.Announcements.Add(a);
        await _context.SaveChangesAsync();

        var told = tellNow ? await _announcements.NotifyAudienceAsync(a) : 0;
        TempData["StatusMessage"] =
            !publish ? "Saved as a draft. Guests can't see it yet." :
            !liveNow ? $"Scheduled for {TimeInput.Display(a.PublishAt, "d MMMM 'at' HH:mm")}." :
            told > 0 ? $"Published, and {told} {(told == 1 ? "guest was" : "guests were")} told." :
            "Published.";
        return RedirectToPage(new { tab = Tab });
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id)
    {
        var me = _userManager.GetUserId(User);
        var a = await _context.Announcements.Include(x => x.Recipients)
            .FirstOrDefaultAsync(x => x.Id == id && x.CreatedByAdminId == me);
        if (a == null)
        {
            TempData["ErrorMessage"] = "You can only remove announcements you posted.";
        }
        else
        {
            _context.Announcements.Remove(a);
            await _context.SaveChangesAsync();
            TempData["StatusMessage"] = "Announcement removed.";
        }
        return RedirectToPage(new { tab = Tab });
    }

    // ---------------------------------------------------------------------------------------------------------------

    /// Checks the form by hand (the dialog is one form for new and edit). Returns a sentence for the guest-facing alert.
    private string? Validate(out DateTime? publishAtUtc)
    {
        publishAtUtc = null;
        F.Intent = F.Intent == "draft" ? "draft" : "publish";
        F.When = F.When == "later" ? "later" : "now";
        F.TierIds ??= new();

        if (string.IsNullOrWhiteSpace(F.Title)) return "Please add a title.";
        if (F.Title.Trim().Length > TitleMax) return $"Keep the title under {TitleMax} characters.";
        if (string.IsNullOrWhiteSpace(F.Content)) return "Please write the message.";
        if (F.Content.Trim().Length > MessageMax) return $"Keep the message under {MessageMax} characters.";

        // A button needs both its words and a page of this site. Only a path on this site is allowed, so a hand-edited form
        // cannot send guests to somewhere else (no "https://...", no "//host", no "javascript:").
        var hasUrl = !string.IsNullOrWhiteSpace(F.ButtonUrl);
        var hasText = !string.IsNullOrWhiteSpace(F.ButtonText);
        if (hasUrl || hasText)
        {
            if (!hasText) return "Give the button a few words, or clear its link.";
            if (!hasUrl) return "Give the button a link, or clear its words.";
            if (F.ButtonText!.Trim().Length > ButtonTextMax) return $"Keep the button words under {ButtonTextMax} characters.";
            var url = F.ButtonUrl!.Trim();
            if (url.Length > ButtonUrlMax) return "That button link is too long.";
            if (!url.StartsWith('/') || url.StartsWith("//") || url.StartsWith("/\\") || url.Any(char.IsControl) || url.Contains(' '))
                return "The button can link to a page of this site only, for example /Menu or /Reservations/Create.";
        }

        if (F.Intent == "publish" && F.When == "later")
        {
            if (F.PublishAt == null) return "Pick the day and time to publish.";
            publishAtUtc = TimeInput.LocalToUtc(F.PublishAt);
            if (publishAtUtc < DateTime.UtcNow.AddMinutes(-1)) return "Pick a time that has not passed yet.";
        }
        return null;
    }

    private async Task<IActionResult> Reopen(string problem)
    {
        ModelState.AddModelError("F", problem);
        await LoadAsync();
        OpenForm = true;
        return Page();
    }
}
