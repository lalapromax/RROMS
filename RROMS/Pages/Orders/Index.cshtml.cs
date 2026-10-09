using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Orders;

public class IndexModel : PageModel
{
    private readonly IOrderService _orderService;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(IOrderService orderService, UserManager<ApplicationUser> userManager)
    {
        _orderService = orderService;
        _userManager = userManager;
    }

    public List<Order> Orders { get; set; } = new();

    public async Task OnGetAsync()
    {
        var userId = _userManager.GetUserId(User)!;
        Orders = await _orderService.GetCustomerOrdersAsync(userId);
    }
}
