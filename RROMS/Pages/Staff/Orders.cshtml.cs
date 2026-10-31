using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Presentation;
using RROMS.Services;

namespace RROMS.Pages.Staff;

/// 
/// The kitchen board: Placed, Preparing, Ready and Served. Each card has one next step. Guests see the change on their own order.
/// "Send to billing" opens the bill for that order; paying it (Billing page) is what closes it.
/// 
public class OrdersModel : PageModel
{
    private readonly IOrderService _orders;
    private readonly UserManager<ApplicationUser> _userManager;

    public OrdersModel(IOrderService orders, UserManager<ApplicationUser> userManager)
    {
        _orders = orders;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)] public int? TableId { get; set; }

    public List<Order> Orders { get; private set; } = new();
    public List<(int Id, string Number)> Tables { get; private set; } = new();

    /// Orders sent ahead for tables that have not been seated yet. They join the board when the table is seated.
    public List<Order> Ahead { get; private set; } = new();

    public IEnumerable<Order> In(OrderStatus s) => Orders.Where(o => o.Status == s && !OrderView.IsWaitingForTable(o));

    public async Task OnGetAsync()
    {
        var all = await _orders.GetActiveOrdersAsync();
        Tables = all.Where(o => o.Table != null).Select(o => (o.Table!.Id, o.Table.TableNumber)).Distinct()
            .OrderBy(t => t.TableNumber.Length).ThenBy(t => t.TableNumber).ToList();
        Orders = TableId.HasValue ? all.Where(o => o.TableId == TableId).ToList() : all;
        Ahead = Orders.Where(OrderView.IsWaitingForTable).ToList();
    }

    /// Only the next step is allowed from the board: Placed, then Preparing, Ready and Served. Nothing skips ahead.
    public async Task<IActionResult> OnPostAdvanceAsync(int id, OrderStatus to)
    {
        var order = await _orders.GetByIdAsync(id);
        if (order == null) return Done(false, "Order not found.");
        if (OrderView.IsWaitingForTable(order)) return Done(false, "That table has not been seated yet. Seat it first and the order comes onto the board.");
        var allowed = order.Status switch
        {
            OrderStatus.Placed => OrderStatus.Preparing,
            OrderStatus.Preparing => OrderStatus.ReadyToServe,
            OrderStatus.ReadyToServe => OrderStatus.Served,
            // a served order that is already paid (paid online, or at the till) can be closed from here; an unpaid one is closed by paying it at Billing
            OrderStatus.Served when order.Payment?.Status == PaymentStatus.Paid => OrderStatus.Completed,
            _ => (OrderStatus?)null
        };
        if (allowed == null || allowed != to) return Done(false, "That order has already moved on. The board is up to date now.");

        var (ok, msg) = await _orders.UpdateOrderStatusAsync(id, to, _userManager.GetUserId(User)!);
        return Done(ok, ok ? (to == OrderStatus.Completed ? "Order closed." : "Moved on. The guest can see it.") : msg);
    }

    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        var (ok, msg) = await _orders.CancelOrderAsync(id, _userManager.GetUserId(User)!, isStaffOrAdmin: true);
        return Done(ok, ok ? "Order cancelled. The guest has been told." : msg);
    }

    private IActionResult Done(bool ok, string message)
    {
        TempData[ok ? "StatusMessage" : "ErrorMessage"] = message;
        return RedirectToPage(new { tableId = TableId });
    }
}
