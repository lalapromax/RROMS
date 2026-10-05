using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Orders;

public class DetailsModel : PageModel
{
    private readonly IOrderService _orderService;
    private readonly UserManager<ApplicationUser> _userManager;

    public DetailsModel(IOrderService orderService, UserManager<ApplicationUser> userManager)
    {
        _orderService = orderService;
        _userManager = userManager;
    }

    public Order? Order { get; set; }
    public bool CanCancel => Order != null && Order.Status == OrderStatus.Placed;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var order = await _orderService.GetByIdAsync(id);
        if (order == null) return NotFound();

        var userId = _userManager.GetUserId(User);
        var isStaffOrAdmin = User.IsInRole(Roles.Staff) || User.IsInRole(Roles.Administrator);
        if (order.CustomerId != userId && !isStaffOrAdmin)
            return Forbid();

        Order = order;
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var isStaffOrAdmin = User.IsInRole(Roles.Staff) || User.IsInRole(Roles.Administrator);
        var (success, message) = await _orderService.CancelOrderAsync(id, userId, isStaffOrAdmin);
        TempData[success ? "StatusMessage" : "ErrorMessage"] = message;
        return RedirectToPage(new { id });
    }
}
