using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Feedback;

/// 
/// "How was your visit?" Customers only: staff and administrators read reviews, they don't write them.
/// The same page edits a review for a week after it was written (<c>?edit=id</c>). Everything posted is re-validated
/// in <see cref="IReviewService"/>; nothing here is trusted.
/// 
[Authorize(Roles = Roles.Customer)]
[RequestSizeLimit(15 * 1024 * 1024)] // 3 photos x 3 MB + the form itself
public class CreateModel : PageModel
{
    private readonly IReviewService _reviewService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;

    public CreateModel(IReviewService reviewService, UserManager<ApplicationUser> userManager, ApplicationDbContext context)
    {
        _reviewService = reviewService;
        _userManager = userManager;
        _context = context;
    }

    [BindProperty]
    public ReviewInputModel Input { get; set; } = new();

    [BindProperty]
    public List<IFormFile> Images { get; set; } = new();

    /// Editing: the id of the review being changed. Null when writing a new one.
    [BindProperty]
    public int? EditId { get; set; }

    /// Editing: the photos already on the review that stay on it.
    [BindProperty]
    public List<string> Keep { get; set; } = new();

    public List<ReviewTarget> Targets { get; private set; } = new();
    public VisitInfo? Visit { get; private set; }

    /// Editing: the photos the review has now, and until when it can be changed.
    public List<string> ExistingImages { get; private set; } = new();
    public DateTime? EditableUntil { get; private set; }

    /// More than one visit is waiting and none was chosen: the page lists them instead of the form.
    public bool NeedsPick => EditId == null && Input.Target == null && Targets.Count > 1;

    public async Task<IActionResult> OnGetAsync(int? orderId, int? reservationId, int? edit, bool pick = false)
    {
        var userId = _userManager.GetUserId(User)!;
        await LoadTargetsAsync();

        if (edit.HasValue)
        {
            var review = await _context.Feedbacks.AsNoTracking().FirstOrDefaultAsync(f => f.Id == edit && f.CustomerId == userId);
            if (review == null) return NotFound();
            if (!review.GuestCanEdit)
            {
                TempData["ErrorMessage"] = $"A review can be changed for {Models.Feedback.GuestEditDays} days after it is written. This one is now closed.";
                return RedirectToPage("/Feedback/Index");
            }

            EditId = review.Id;
            EditableUntil = review.EditableUntil.ToLocalTime();
            ExistingImages = review.ImageList().ToList();
            Keep = ExistingImages.ToList();
            Input = new ReviewInputModel
            {
                Target = review.OrderId.HasValue ? $"order:{review.OrderId}" : $"reservation:{review.ReservationId}",
                Food = review.FoodRating, Service = review.ServiceRating, Cleanliness = review.CleanlinessRating,
                Wait = review.WaitTimeRating, Value = review.ValueRating, RecommendScore = review.RecommendScore,
                IssueTags = review.IssueTagList().Where(t => ReviewOptions.IssueTags.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList(),
                Comments = review.Comments, ShowFirstName = review.ShowFirstName
            };
            await LoadVisitAsync(userId);
            return Page();
        }

        string? wanted = orderId.HasValue ? $"order:{orderId}" : reservationId.HasValue ? $"reservation:{reservationId}" : null;
        if (wanted != null)
        {
            if (Targets.Any(t => t.Key == wanted))
                Input.Target = wanted;
            else
                TempData["ErrorMessage"] = "That order or visit can't be reviewed. It may already have a review, or it isn't finished yet.";
        }
        else if (Targets.Count == 1 && !pick)
        {
            Input.Target = Targets[0].Key;
        }

        await LoadVisitAsync(userId);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var userId = _userManager.GetUserId(User)!;
        await LoadTargetsAsync();
        if (EditId.HasValue) await LoadEditContextAsync(userId);

        if (!ModelState.IsValid)
        {
            await LoadVisitAsync(userId);
            return Page();
        }

        var submission = new ReviewSubmission
        {
            Target = Input.Target,
            Food = Input.Food,
            Service = Input.Service,
            Cleanliness = Input.Cleanliness,
            Wait = Input.Wait,
            Value = Input.Value,
            RecommendScore = Input.RecommendScore,
            IssueTags = Input.IssueTags,
            Comments = Input.Comments,
            ShowFirstName = Input.ShowFirstName,
            Images = Images,
            KeepImages = Keep
        };

        var (success, message) = EditId.HasValue
            ? await _reviewService.UpdateAsync(userId, EditId.Value, submission)
            : await _reviewService.SubmitAsync(userId, submission);

        if (!success)
        {
            // Browsers can't pre-fill file inputs, so say so rather than silently dropping the new photos.
            var extra = Images.Any(i => i.Length > 0) ? " Your new photos weren't kept, so please add them again." : string.Empty;
            ModelState.AddModelError(string.Empty, message + extra);
            await LoadVisitAsync(userId);
            return Page();
        }

        TempData["StatusMessage"] = EditId.HasValue ? message : "Thank you. Your review is in.";
        return RedirectToPage("/Feedback/Index");
    }

    private async Task LoadTargetsAsync()
    {
        var userId = _userManager.GetUserId(User)!;
        Targets = await _reviewService.GetEligibleTargetsAsync(userId);
    }

    /// On a failed edit the page needs the photos and deadline again.
    private async Task LoadEditContextAsync(string userId)
    {
        var review = await _context.Feedbacks.AsNoTracking().FirstOrDefaultAsync(f => f.Id == EditId && f.CustomerId == userId);
        if (review == null) return;
        EditableUntil = review.EditableUntil.ToLocalTime();
        ExistingImages = review.ImageList().ToList();
    }

    /// What the "Your visit" card shows: the order or the dining visit being reviewed.
    private async Task LoadVisitAsync(string userId)
    {
        var parts = (Input.Target ?? string.Empty).Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var id)) return;

        if (parts[0] == "order")
        {
            var order = await _context.Orders.AsNoTracking()
                .Include(o => o.Table)
                .Include(o => o.Reservation)
                .Include(o => o.OrderItems).ThenInclude(i => i.MenuItem).ThenInclude(m => m!.MenuCategory)
                .FirstOrDefaultAsync(o => o.Id == id && o.CustomerId == userId);
            if (order == null) return;
            var star = order.OrderItems.Where(i => i.Serving == null).OrderByDescending(i => i.UnitPrice).FirstOrDefault();
            Visit = new VisitInfo(
                "Order", "#" + order.Id, order.OrderDate.ToLocalTime(),
                order.OrderType == OrderType.Takeaway ? "To collect" : TableText(order.Table?.TableNumber, order.Reservation?.PartySize),
                star?.MenuItem?.ImageUrl, star?.ItemNameSnapshot, star?.MenuItem?.MenuCategory?.Name);
        }
        else if (parts[0] == "reservation")
        {
            var r = await _context.Reservations.AsNoTracking().Include(x => x.Table)
                .FirstOrDefaultAsync(x => x.Id == id && x.CustomerId == userId);
            if (r == null) return;
            Visit = new VisitInfo("Visit", "", r.ReservationDateTime, TableText(r.Table?.TableNumber, r.PartySize), null, null, null);
        }
    }

    private static string TableText(string? number, int? party) =>
        (number == null ? "To be set" : number) + (party.HasValue ? ", for " + Presentation.OrderView.Words(party.Value) : "");

    /// The line items of the "Your visit" card.
    public record VisitInfo(string Kind, string Number, DateTime When, string TableText, string? PicUrl, string? PicName, string? PicCategory);

    public class ReviewInputModel
    {
        [Required(ErrorMessage = "Please choose what you are reviewing.")]
        public string? Target { get; set; }

        [Range(1, 5)] public int? Food { get; set; }
        [Range(1, 5)] public int? Service { get; set; }
        [Range(1, 5)] public int? Cleanliness { get; set; }
        [Range(1, 5)] public int? Wait { get; set; }
        [Range(1, 5)] public int? Value { get; set; }

        [Range(0, 10)]
        public int? RecommendScore { get; set; }

        public List<string> IssueTags { get; set; } = new();

        [StringLength(ReviewOptions.MaxCommentLength, ErrorMessage = "Please keep your comments under 1000 characters.")]
        public string? Comments { get; set; }

        public bool ShowFirstName { get; set; } = true;
    }
}
