using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Presentation;
using RROMS.Services;

namespace RROMS.Pages.Admin;

/// 
/// The administrator's announcements. Unlike staff, an administrator can reach everyone (including visitors who are not
/// signed in), all staff, or named people, and can attach a picture. The server checks every audience choice again in
/// <see cref="IAnnouncementService.ApplyAudienceAsync"/>. Something that is not published is a draft; it can be pinned to the
/// top of guests' Notices and may carry one button that links to a page of this site.
/// 
[RequestSizeLimit(6 * 1024 * 1024)] // pictures are capped at 4 MB by IAdminImageService; this just stops oversized bodies early
public class AnnouncementsModel : PageModel
{
    public const int TitleMax = 150, MessageMax = 1000, ButtonTextMax = 40, ButtonUrlMax = 200;

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAnnouncementService _announcements;
    private readonly IRewardService _rewards;
    private readonly IAdminImageService _images;

    public AnnouncementsModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager,
        IAnnouncementService announcements, IRewardService rewards, IAdminImageService images)
    {
        _context = context;
        _userManager = userManager;
        _announcements = announcements;
        _rewards = rewards;
        _images = images;
    }

    [BindProperty(SupportsGet = true)] public string? Tab { get; set; }
    [BindProperty(Name = "F")] public AnnouncementInput F { get; set; } = new();
    [BindProperty(Name = "ImageFile")] public IFormFile? ImageFile { get; set; }

    public IReadOnlyList<TierInfo> Tiers { get; private set; } = Array.Empty<TierInfo>();
    public List<Row> Rows { get; private set; } = new();
    public Dictionary<string, int> Counts { get; private set; } = new();
    /// True when the editor should open again because a save was refused; <see cref="F"/> still holds what was typed.
    public bool OpenForm { get; private set; }
    public string? FormError { get; private set; }
    /// The names behind the named-people chips when the editor reopens.
    public Dictionary<string, string> PeopleNames { get; private set; } = new();
    /// The picture the announcement being edited has now, read from the database (never from the form).
    public string? CurrentImage { get; private set; }
    /// True when the announcement being edited has already gone live (so there is nothing left to send a notice about).
    public bool CurrentLive { get; private set; }

    public static readonly (string Key, string Label)[] Tabs =
    {
        ("all", "All"), ("published", "Published"), ("scheduled", "Scheduled"), ("drafts", "Drafts")
    };

    public record Row(Announcement A, string State, string Css, string Audience, string When, string? Author, int Told, int Read);

    public class AnnouncementInput
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public string? Content { get; set; }
        public bool Public { get; set; }
        public bool AllCustomers { get; set; }
        public bool AllStaff { get; set; }
        public List<int> TierIds { get; set; } = new();
        public List<string> UserIds { get; set; } = new();
        /// Blank means now.
        public DateTime? PublishAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool Pinned { get; set; }
        public string? ButtonText { get; set; }
        public string? ButtonUrl { get; set; }
        /// Also send each person a notice when this goes live.
        public bool Notify { get; set; }
        /// Take the current picture off (a new file, if one is chosen, replaces it either way).
        public bool RemoveImage { get; set; }
        /// "publish" or "draft".
        public string Intent { get; set; } = "publish";
    }

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        Tiers = await _rewards.GetTiersAsync();
        var tierNames = Tiers.ToDictionary(t => t.Id, t => t.Name);

        var list = await _context.Announcements.AsNoTracking()
            .Include(a => a.Recipients).ThenInclude(r => r.User)
            .OrderByDescending(a => a.IsPinned).ThenByDescending(a => a.PublishAt ?? a.CreatedAt)
            .Take(200)
            .ToListAsync();

        var authorIds = list.Where(a => a.CreatedByAdminId != null).Select(a => a.CreatedByAdminId!).Distinct().ToList();
        var authors = await _context.Users.AsNoTracking().Where(u => authorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);

        // How many guests were told by notice, and how many opened it. A guest who only reads it in Notices is not counted.
        var links = list.ToDictionary(a => a.Id, a => "/Notifications/Index#a" + a.Id);
        var linkList = links.Values.ToList();
        var seen = await _context.Notifications.AsNoTracking()
            .Where(n => n.LinkUrl != null && linkList.Contains(n.LinkUrl))
            .GroupBy(n => n.LinkUrl!)
            .Select(g => new { Link = g.Key, Told = g.Count(), Read = g.Count(x => x.IsRead) })
            .ToDictionaryAsync(x => x.Link);

        var now = DateTime.UtcNow;
        var all = list.Select(a =>
        {
            var state = AnnouncementLabels.State(a, now);
            seen.TryGetValue(links[a.Id], out var s);
            var author = a.CreatedByAdminId != null && authors.TryGetValue(a.CreatedByAdminId, out var n)
                ? n + (a.CreatedByRole == Roles.Staff ? " (staff)" : "")
                : null;
            return new Row(a, state, AnnouncementLabels.Css(state), AnnouncementLabels.Audience(a, tierNames),
                AnnouncementLabels.When(a, now), author, s?.Told ?? 0, s?.Read ?? 0);
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

    /// Type-ahead for the "specific people" picker. Admin-only (folder policy); never returns admins.
    public async Task<IActionResult> OnGetPeopleAsync(string? q, int? tierId)
    {
        var list = await _announcements.SearchPeopleAsync(q, tierId, includeStaff: true, take: 15);
        return new JsonResult(list.Select(p => new { id = p.Id, name = p.Name, email = p.Email, role = p.Role, tierName = p.TierName }));
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var problem = Validate(out var publishAtUtc, out var expiresAtUtc);
        if (problem != null) return await Reopen(problem);

        var publish = F.Intent == "publish";
        var now = DateTime.UtcNow;

        Announcement a;
        var wasLive = false;
        if (F.Id > 0)
        {
            var found = await _context.Announcements.Include(x => x.Recipients).FirstOrDefaultAsync(x => x.Id == F.Id);
            if (found == null)
            {
                TempData["ErrorMessage"] = "That announcement is no longer there.";
                return RedirectToPage(new { tab = Tab });
            }
            a = found;
            wasLive = a.IsActive && (a.PublishAt == null || a.PublishAt <= now);
        }
        else
        {
            a = new Announcement { CreatedAt = now, CreatedByAdminId = _userManager.GetUserId(User), CreatedByRole = Roles.Administrator };
        }

        var audienceProblem = await _announcements.ApplyAudienceAsync(a,
            new AudienceInput(F.Public, F.AllCustomers, F.AllStaff, F.TierIds ?? new(), F.UserIds ?? new()), authorIsAdmin: true);
        if (audienceProblem != null) return await Reopen(audienceProblem);

        // A new file replaces the picture; "Remove the picture" clears it; otherwise the current one stays.
        var previousImage = a.ImageUrl;
        var keep = F.RemoveImage ? null : (F.Id > 0 ? previousImage : null);
        var (imageOk, imageMessage, resolvedImage) = await _images.ResolveAsync(ImageFile, keep, "announcements");
        if (!imageOk) return await Reopen(imageMessage);

        a.Title = F.Title!.Trim();
        a.Content = F.Content!.Trim();
        a.ImageUrl = resolvedImage;
        a.IsPinned = F.Pinned;
        a.ButtonText = string.IsNullOrWhiteSpace(F.ButtonUrl) ? null : F.ButtonText!.Trim();
        a.ButtonUrl = string.IsNullOrWhiteSpace(F.ButtonUrl) ? null : F.ButtonUrl.Trim();
        a.IsActive = publish;
        a.PublishAt = publishAtUtc;
        a.ExpiresAt = expiresAtUtc;

        var liveNow = publish && (a.PublishAt == null || a.PublishAt <= now);
        var tellNow = false;
        if (!publish || !F.Notify || wasLive) a.NotifyPending = false;
        else if (liveNow) { a.NotifyPending = false; tellNow = true; }
        else a.NotifyPending = true;

        if (a.Id == 0) _context.Announcements.Add(a);
        await _context.SaveChangesAsync();

        if (previousImage != resolvedImage) _images.DeleteIfOurs(previousImage);

        var told = tellNow ? await _announcements.NotifyAudienceAsync(a) : 0;
        TempData["StatusMessage"] =
            !publish ? "Saved as a draft. Nobody can see it yet." :
            !liveNow ? $"Scheduled for {TimeInput.Display(a.PublishAt, "d MMMM 'at' HH:mm")}." :
            told > 0 ? $"Published, and {told} {(told == 1 ? "person was" : "people were")} told." :
            "Published.";
        return RedirectToPage(new { tab = Tab });
    }

    /// Unpublish something that is live or scheduled, or publish a draft straight away.
    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var a = await _context.Announcements.FindAsync(id);
        if (a == null) return RedirectToPage(new { tab = Tab });

        a.IsActive = !a.IsActive;
        if (!a.IsActive) a.NotifyPending = false;
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = a.IsActive ? "Published." : "Unpublished. It is now a draft.";
        return RedirectToPage(new { tab = Tab });
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id)
    {
        var a = await _context.Announcements.FindAsync(id);
        if (a != null)
        {
            _context.Announcements.Remove(a);
            await _context.SaveChangesAsync();
            _images.DeleteIfOurs(a.ImageUrl);
            TempData["StatusMessage"] = "Announcement removed.";
        }
        return RedirectToPage(new { tab = Tab });
    }

    // ---------------------------------------------------------------------------------------------------------------

    /// Checks the form by hand (the dialog is one form for new and edit). Returns a sentence for the alert at the top of the dialog.
    private string? Validate(out DateTime? publishAtUtc, out DateTime? expiresAtUtc)
    {
        publishAtUtc = null;
        expiresAtUtc = null;
        F.Intent = F.Intent == "draft" ? "draft" : "publish";
        F.TierIds ??= new();
        F.UserIds ??= new();

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

        publishAtUtc = TimeInput.LocalToUtc(F.PublishAt);
        expiresAtUtc = TimeInput.LocalToUtc(F.ExpiresAt);
        if (publishAtUtc.HasValue && expiresAtUtc.HasValue && expiresAtUtc <= publishAtUtc) return "It has to end after it starts.";
        if (expiresAtUtc.HasValue && F.Intent == "publish" && expiresAtUtc < DateTime.UtcNow) return "That end time has already passed.";
        return null;
    }

    private async Task<IActionResult> Reopen(string problem)
    {
        await LoadAsync();
        OpenForm = true;
        FormError = problem;

        // Names for the people already chosen, so their chips come back with the form.
        var ids = (F.UserIds ?? new()).Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().Take(100).ToList();
        if (F.Id > 0)
        {
            var was = await _context.Announcements.AsNoTracking().Where(x => x.Id == F.Id)
                .Select(x => new { x.ImageUrl, x.IsActive, x.PublishAt }).FirstOrDefaultAsync();
            CurrentImage = was?.ImageUrl;
            CurrentLive = was != null && was.IsActive && (was.PublishAt == null || was.PublishAt <= DateTime.UtcNow);
        }
        if (ids.Count > 0)
            PeopleNames = await _context.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
        return Page();
    }
}
