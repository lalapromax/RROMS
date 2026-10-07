using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;
using RROMS.ViewModels;

namespace RROMS.Pages.Staff;

/// <summary>
/// Add an order for a table that is already seated, or for someone at the counter. It goes through the same
/// <see cref="IOrderService.PlaceOrderAsync"/> as an online order, so prices, discounts and checks are the same. Set menus are
/// chosen by guests from the Menu page; here staff add single dishes and wines.
/// </summary>
public class NewOrderModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly IOrderService _orders;

    public NewOrderModel(ApplicationDbContext context, IOrderService orders)
    {
        _context = context;
        _orders = orders;
    }

    [BindProperty] public int ReservationId { get; set; }
    [BindProperty] public string? CounterName { get; set; }
    [BindProperty] public string? Notes { get; set; }
    [BindProperty] public Dictionary<int, int> Dish { get; set; } = new();
    [BindProperty] public Dictionary<int, int> Glass { get; set; } = new();
    [BindProperty] public Dictionary<int, int> Bottle { get; set; } = new();

    [BindProperty(SupportsGet = true)] public int? For { get; set; }

    public List<Reservation> Open { get; private set; } = new();
    public List<MenuCategory> Categories { get; private set; } = new();
    public string? Error { get; private set; }

    public async Task OnGetAsync()
    {
        if (For.HasValue) ReservationId = For.Value;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var start = DateTime.Today;
        Open = await _context.Reservations.AsNoTracking()
            .Include(r => r.Table).Include(r => r.Customer)
            .Where(r => r.ReservationDateTime >= start && r.ReservationDateTime < start.AddDays(1)
                        && (r.Status == ReservationStatus.Seated || r.Status == ReservationStatus.Confirmed))
            .OrderByDescending(r => r.Status == ReservationStatus.Seated).ThenBy(r => r.ReservationDateTime)
            .ToListAsync();
        Categories = await _context.MenuCategories.AsNoTracking()
            .Include(c => c.MenuItems.Where(i => i.IsAvailable))
            .Where(c => c.IsActive && !c.SetMenuOnly)
            .OrderBy(c => c.DisplayOrder).ToListAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var items = new List<CartItemVM>();
        foreach (var (id, q) in Dish.Where(x => x.Value > 0)) items.Add(new CartItemVM { Kind = CartKind.Dish, MenuItemId = id, Quantity = Math.Min(q, 50) });
        foreach (var (id, q) in Glass.Where(x => x.Value > 0)) items.Add(new CartItemVM { Kind = CartKind.WineGlass, MenuItemId = id, Quantity = Math.Min(q, 50) });
        foreach (var (id, q) in Bottle.Where(x => x.Value > 0)) items.Add(new CartItemVM { Kind = CartKind.WineBottle, MenuItemId = id, Quantity = Math.Min(q, 50) });

        if (items.Count == 0) return await Fail("Choose at least one dish or wine.");

        string customerId;
        OrderType type;
        int? reservationId = null;
        var notes = Notes?.Trim();
        if (ReservationId > 0)
        {
            var r = await _context.Reservations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ReservationId);
            if (r == null || r.Status is not (ReservationStatus.Seated or ReservationStatus.Confirmed))
                return await Fail("That booking is not open any more. Choose another table.");
            customerId = r.CustomerId;
            reservationId = r.Id;
            type = OrderType.DineIn;
        }
        else
        {
            var walkIn = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == ApplicationUser.WalkInEmail);
            if (walkIn == null) return await Fail("The walk-in account is missing. Restart the site once so it is created.");
            customerId = walkIn.Id;
            type = OrderType.Takeaway;
            if (!string.IsNullOrWhiteSpace(CounterName)) notes = ("For " + CounterName.Trim() + ". " + notes).Trim();
        }
        if (notes != null && notes.Length > 400) return await Fail("Keep the notes under 400 characters.");

        var (ok, msg, order) = await _orders.PlaceOrderAsync(customerId, items, type, reservationId, notes, PaymentMethod.Cash);
        if (!ok) return await Fail(msg);

        TempData["StatusMessage"] = order?.Table != null ? $"Order added for table {order.Table.TableNumber}." : "Order added for the counter.";
        return RedirectToPage("/Staff/Orders");
    }

    private async Task<IActionResult> Fail(string message)
    {
        Error = message;
        await LoadAsync();
        return Page();
    }
}
