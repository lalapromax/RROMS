using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Feedback;

/// A guest's own reviews, with the team's replies. Customers only; each guest sees only their own.
[Authorize(Roles = Roles.Customer)]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IReviewService _reviewService;

    public IndexModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IReviewService reviewService)
    {
        _context = context;
        _userManager = userManager;
        _reviewService = reviewService;
    }

    /// One review as the card draws it: "Friday dinner", "16 Oct 2026 · Order #2841".
    public record ReviewCard(Models.Feedback Review, string Title, string When);

    public List<ReviewCard> Cards { get; private set; } = new();
    public double Average { get; private set; }

    /// Orders and visits still waiting for a review.
    public List<ReviewTarget> Waiting { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var userId = _userManager.GetUserId(User)!;
        var mine = await _context.Feedbacks.AsNoTracking()
            .Include(f => f.Order)
            .Include(f => f.Reservation)
            .Where(f => f.CustomerId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync();

        Cards = mine.Select(f =>
        {
            var when = f.Order != null ? f.Order.OrderDate.ToLocalTime()
                     : f.Reservation != null ? f.Reservation.ReservationDateTime
                     : f.CreatedAt.ToLocalTime();
            var meal = when.Hour < 16 ? "lunch" : "dinner";
            var what = f.Order != null ? $" · Order #{f.Order.Id}" : "";
            return new ReviewCard(f, $"{when:dddd} {meal}", $"{when:d MMM yyyy}{what}");
        }).ToList();
        Average = mine.Count == 0 ? 0 : Math.Round(mine.Average(f => f.Rating), 1);
        Waiting = await _reviewService.GetEligibleTargetsAsync(userId);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var (success, message) = await _reviewService.DeleteAsync(userId, id);
        TempData[success ? "StatusMessage" : "ErrorMessage"] = message;
        return RedirectToPage();
    }
}
