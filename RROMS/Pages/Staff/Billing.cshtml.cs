using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Staff;

/// 
/// Settle a table. The left list is every bill still to be paid; the right side is the one being settled. Paying records the method,
/// closes the order if it has been served, and gives the guest their points. The guest can also pay online, and then the bill is already paid.
/// 
public class BillingModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly IOrderService _orders;
    private readonly IRewardService _rewards;
    private readonly UserManager<ApplicationUser> _userManager;

    public BillingModel(ApplicationDbContext context, IOrderService orders, IRewardService rewards, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _orders = orders;
        _rewards = rewards;
        _userManager = userManager;
    }

    /// The bill being looked at. The Orders board and the receipt page link here with it.
    [BindProperty(SupportsGet = true)] public int? SearchOrderId { get; set; }

    public List<Order> Open { get; private set; } = new();
    public Order? Current { get; private set; }
    public string? Tier { get; private set; }
    public string? NotFound { get; private set; }

    public async Task OnGetAsync()
    {
        Open = await _context.Orders.AsNoTracking()
            .Include(o => o.Table).Include(o => o.Payment).Include(o => o.Customer).Include(o => o.OrderItems)
            .Where(o => o.Status != OrderStatus.Cancelled && (o.Payment == null || o.Payment.Status == PaymentStatus.Pending))
            .ToListAsync();
        // Served first (those are waiting for the bill), then the oldest
        Open = Open.OrderByDescending(o => o.Status == OrderStatus.Served).ThenBy(o => o.OrderDate).ToList();

        if (SearchOrderId.HasValue)
        {
            Current = await _orders.GetByIdAsync(SearchOrderId.Value);
            if (Current == null) NotFound = "There is no order with that number.";
        }
        Current ??= Open.Count > 0 && !SearchOrderId.HasValue ? await _orders.GetByIdAsync(Open[0].Id) : null;

        if (Current?.Customer is { IsSystemAccount: false } c && c.IsActive)
            Tier = (await _rewards.GetTierForPointsAsync(await _rewards.GetPointsAsync(c.Id))).Name;
    }

    public async Task<IActionResult> OnPostPayAsync(int orderId, PaymentMethod method)
    {
        if (method is not (PaymentMethod.Card or PaymentMethod.Cash or PaymentMethod.QrPayment))
        {
            TempData["ErrorMessage"] = "Choose how the guest is paying.";
            return RedirectToPage(new { searchOrderId = orderId });
        }
        var staffId = _userManager.GetUserId(User)!;
        var (ok, msg) = await _orders.RecordPaymentAsync(orderId, method, staffId, null);
        if (ok)
        {
            // A served table is finished once it is paid. Anything still in the kitchen stays on the board until it is served.
            var order = await _orders.GetByIdAsync(orderId);
            if (order is { Status: OrderStatus.Served })
                await _orders.UpdateOrderStatusAsync(orderId, OrderStatus.Completed, staffId);
        }
        TempData[ok ? "StatusMessage" : "ErrorMessage"] = ok ? "Paid. The guest's points are added." : msg;
        return RedirectToPage(new { });
    }
}
