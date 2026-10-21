using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Presentation;
using RROMS.Services;
using RROMS.ViewModels;

namespace RROMS.Pages.Admin;

/// Everything guests have written, newest first, with the figures behind it and a place to answer. A reply shows under the review on the guest's own Reviews page and they get a notice.
public class ReviewsModel : PageModel
{
    private const int PageSize = 15;

    private readonly ApplicationDbContext _context;
    private readonly IReportService _reports;
    private readonly INotificationService _notifications;
    private readonly IAuditService _audit;
    private readonly UserManager<ApplicationUser> _users;

    public ReviewsModel(ApplicationDbContext context, IReportService reports, INotificationService notifications, IAuditService audit, UserManager<ApplicationUser> users)
    {
        _context = context;
        _reports = reports;
        _notifications = notifications;
        _audit = audit;
        _users = users;
    }

    [BindProperty(SupportsGet = true)] public DateTime StartDate { get; set; } = DateTime.Today.AddDays(-29);
    [BindProperty(SupportsGet = true)] public DateTime EndDate { get; set; } = DateTime.Today;
    /// 0 = all, 1-5 = exactly that many, 3 with Low = "3 and below" is not offered: keep it simple, one value.
    [BindProperty(SupportsGet = true)] public int Stars { get; set; }
    [BindProperty(SupportsGet = true)] public string? Issue { get; set; }
    /// "waiting" or "replied"; anything else shows both.
    [BindProperty(SupportsGet = true)] public string? Reply { get; set; }
    [BindProperty(SupportsGet = true)] public bool OnlyComments { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNo { get; set; } = 1;

    public record Item(RROMS.Models.Feedback F, string Name, string FirstName, string Context);

    public List<Item> Items { get; private set; } = new();
    public FeedbackSummaryVM Summary { get; private set; } = new();
    public int Total { get; private set; }
    public int WithComments { get; private set; }
    public int Waiting { get; private set; }
    public int Pages { get; private set; } = 1;
    public Dictionary<string, string?> Keep => new()
    {
        ["StartDate"] = StartDate.ToString("yyyy-MM-dd"),
        ["EndDate"] = EndDate.ToString("yyyy-MM-dd"),
        ["Stars"] = Stars == 0 ? null : Stars.ToString(),
        ["Issue"] = Issue,
        ["Reply"] = Reply,
        ["OnlyComments"] = OnlyComments ? "true" : null,
        ["PageNo"] = PageNo > 1 ? PageNo.ToString() : null
    };

    public async Task OnGetAsync()
    {
        var today = DateTime.Today;
        StartDate = StartDate.Date; EndDate = EndDate.Date;
        if (EndDate < StartDate) (StartDate, EndDate) = (EndDate, StartDate);
        if (EndDate > today) EndDate = today;
        if (StartDate > EndDate) StartDate = EndDate;
        if ((EndDate - StartDate).TotalDays > 730) StartDate = EndDate.AddDays(-730);
        if (Stars < 0 || Stars > 5) Stars = 0;
        if (!string.IsNullOrWhiteSpace(Issue) && !ReviewOptions.IssueTags.Contains(Issue)) Issue = null;

        Summary = await _reports.GetFeedbackSummaryAsync(StartDate, EndDate);

        var fromUtc = DateTime.SpecifyKind(StartDate, DateTimeKind.Local).ToUniversalTime();
        var toUtc = DateTime.SpecifyKind(EndDate.AddDays(1), DateTimeKind.Local).ToUniversalTime();
        var period = _context.Feedbacks.AsNoTracking().Where(f => f.CreatedAt >= fromUtc && f.CreatedAt < toUtc);
        Total = await period.CountAsync();
        WithComments = await period.CountAsync(f => f.Comments != null && f.Comments != "");
        Waiting = await period.CountAsync(f => f.Comments != null && f.Comments != "" && f.StaffResponse == null);

        var q = period;
        if (Stars > 0) q = q.Where(f => f.Rating == Stars);
        if (!string.IsNullOrWhiteSpace(Issue)) q = q.Where(f => f.IssueTags != null && f.IssueTags.Contains(Issue));
        if (OnlyComments) q = q.Where(f => f.Comments != null && f.Comments != "");
        if (Reply == "waiting") q = q.Where(f => f.StaffResponse == null);
        else if (Reply == "replied") q = q.Where(f => f.StaffResponse != null);

        var count = await q.CountAsync();
        Pages = Math.Max(1, (count + PageSize - 1) / PageSize);
        PageNo = Math.Clamp(PageNo, 1, Pages);

        var rows = await q.Include(f => f.Customer).OrderByDescending(f => f.CreatedAt)
            .Skip((PageNo - 1) * PageSize).Take(PageSize).ToListAsync();
        Items = rows.Select(f =>
        {
            var full = f.Customer?.FullName?.Trim() ?? "";
            var parts = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var first = parts.Length > 0 ? parts[0] : "";
            var shown = !f.ShowFirstName || first.Length == 0 ? "A guest" : parts.Length > 1 ? $"{first} {parts[^1][0]}." : first;
            var ctx = f.OrderId.HasValue ? $"Order #{f.OrderId}" : f.ReservationId.HasValue ? "Dining visit" : "";
            return new Item(f, shown, f.ShowFirstName ? first : "", ctx);
        }).ToList();
    }

    public async Task<IActionResult> OnPostReplyAsync(int id, string? reply)
    {
        var text = (reply ?? "").Trim();
        if (text.Length == 0 || text.Length > 600)
        {
            TempData["ErrorMessage"] = text.Length == 0 ? "Write a few words to reply." : "A reply can be 600 characters at most.";
            return RedirectToPage(Keep.Where(k => k.Value != null).ToDictionary(k => k.Key, k => (object?)k.Value));
        }
        var f = await _context.Feedbacks.Include(x => x.Customer).FirstOrDefaultAsync(x => x.Id == id);
        if (f == null) { TempData["ErrorMessage"] = "That review no longer exists."; return RedirectToPage(); }

        var first = string.IsNullOrWhiteSpace(f.StaffResponse);
        var acting = await _users.GetUserAsync(User);
        f.StaffResponse = text;
        f.RespondedAt = DateTime.UtcNow;
        f.RespondedByStaffId = acting?.Id;
        await _context.SaveChangesAsync();

        if (first)
            await _notifications.NotifyAsync(f.CustomerId, "We replied to your review", $"{Brand.Name} has answered the review you wrote. You can read it under your reviews.", NotificationType.System, "/Feedback/Index");
        if (acting != null) await _audit.LogAsync(acting.Id, acting.FullName, "ReplyToReview", $"Review #{f.Id}");

        TempData["StatusMessage"] = first ? "Your reply was sent." : "Your reply was changed.";
        return RedirectToPage(Keep.Where(k => k.Value != null).ToDictionary(k => k.Key, k => (object?)k.Value));
    }
}
