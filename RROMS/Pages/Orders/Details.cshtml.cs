using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Orders;

/// 
/// The order and its receipt. One page for three kinds of reader: the guest who placed it, and staff or an administrator
/// looking it up for them. The page model refuses everyone else.
/// 
public class DetailsModel : PageModel
{
    private readonly IOrderService _orderService;
    private readonly IRewardService _rewards;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;

    public DetailsModel(IOrderService orderService, IRewardService rewards, UserManager<ApplicationUser> userManager, ApplicationDbContext context)
    {
        _orderService = orderService;
        _rewards = rewards;
        _userManager = userManager;
        _context = context;
    }

    public Order Order { get; private set; } = null!;

    public bool IsOwner { get; private set; }
    public bool IsStaff => User.IsInRole(Roles.Staff);
    public bool IsAdmin => User.IsInRole(Roles.Administrator);

    /// True only for the guest who placed a completed order that has no review yet.
    public bool CanReview { get; private set; }
    public bool AlreadyReviewed { get; private set; }

    /// Guests can cancel until the kitchen starts; staff and administrators until the order is closed.
    public bool CanCancel =>
        Order.Status != OrderStatus.Cancelled && Order.Status != OrderStatus.Completed
        && (Order.Status == OrderStatus.Placed || IsStaff || IsAdmin);

    /// The promo code the guest used on this order, if any.
    public string? CodeUsed { get; private set; }

    /// Points already added for this order, or what it will add once it is finished and paid.
    public int PointsAdded { get; private set; }
    public int PointsToCome { get; private set; }

    /// How many guests the booking behind this order is for (for the line under the title).
    public int? PartySize => Order.Reservation?.PartySize;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var order = await _orderService.GetByIdAsync(id);
        if (order == null) return NotFound();

        var userId = _userManager.GetUserId(User);
        IsOwner = order.CustomerId == userId;
        if (!IsOwner && !IsStaff && !IsAdmin)
            return Forbid();

        Order = order;
        CodeUsed = order.PromotionRedemptions.Select(r => r.Promotion?.Code).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

        PointsAdded = await _context.RewardLedger.Where(r => r.OrderId == order.Id).SumAsync(r => (int?)r.Points) ?? 0;
        if (PointsAdded == 0 && order.Status != OrderStatus.Cancelled)
            PointsToCome = (int)Math.Floor(order.TotalAmount) * _rewards.PointsPerCurrencyUnit;

        if (order.Status == OrderStatus.Completed && IsOwner)
        {
            AlreadyReviewed = await _context.Feedbacks.AnyAsync(f => f.OrderId == order.Id);
            CanReview = !AlreadyReviewed;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var (success, message) = await _orderService.CancelOrderAsync(id, userId, IsStaff || IsAdmin);
        TempData[success ? "StatusMessage" : "ErrorMessage"] = message;
        return RedirectToPage(new { id });
    }
}
