using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;
using RROMS.ViewModels;

namespace RROMS.Pages.Orders;

public class CartModel : PageModel
{
    private const string CartSessionKey = "Cart";

    private readonly IOrderService _orderService;
    private readonly IReservationService _reservationService;
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public CartModel(IOrderService orderService, IReservationService reservationService, ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _orderService = orderService;
        _reservationService = reservationService;
        _context = context;
        _userManager = userManager;
    }

    public List<CartItemVM> CartItems { get; set; } = new();
    public decimal SubTotal => CartItems.Sum(c => c.Subtotal);
    public decimal EstimatedTax { get; set; }
    public decimal EstimatedTotal => SubTotal + EstimatedTax;
    public List<Reservation> UpcomingReservations { get; set; } = new();

    [BindProperty]
    public CheckoutVM Checkout { get; set; } = new();

    /// <summary>Lets a dine-in customer reserve a table without leaving the cart,
    /// so booking and ordering can happen as one flow instead of two separate pages.</summary>
    [BindProperty]
    public ReservationFormVM QuickReserve { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? ReservationId { get; set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
        if (ReservationId.HasValue)
            Checkout.ReservationId = ReservationId;
    }

    public async Task<IActionResult> OnPostUpdateQuantityAsync(int menuItemId, int quantity)
    {
        var cart = GetCart();
        var item = cart.FirstOrDefault(c => c.MenuItemId == menuItemId);
        if (item != null)
        {
            if (quantity <= 0) cart.Remove(item);
            else item.Quantity = quantity;
        }
        SaveCart(cart);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int menuItemId)
    {
        var cart = GetCart();
        cart.RemoveAll(c => c.MenuItemId == menuItemId);
        SaveCart(cart);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostQuickReserveAsync()
    {
        if (!TimeSpan.TryParse(QuickReserve.ReservationTime, out var time))
        {
            TempData["ErrorMessage"] = "Please choose a valid time for your table.";
            return RedirectToPage();
        }

        var userId = _userManager.GetUserId(User)!;
        var (success, message, reservation) = await _reservationService.CreateReservationAsync(
            userId, QuickReserve.ReservationDate, time, QuickReserve.PartySize, QuickReserve.SpecialRequests, diningAreaId: null);

        if (!success)
        {
            TempData["ErrorMessage"] = message;
            return RedirectToPage();
        }

        TempData["StatusMessage"] = $"Table reserved for {reservation!.ReservationDateTime:dddd, dd MMM 'at' h:mm tt} - attached to your order below.";
        return RedirectToPage(new { ReservationId = reservation.Id });
    }

    public async Task<IActionResult> OnPostCheckoutAsync()
    {
        var cart = GetCart();
        if (cart.Count == 0)
        {
            TempData["ErrorMessage"] = "Your cart is empty.";
            return RedirectToPage();
        }

        if (Checkout.OrderType == OrderType.DineIn && !Checkout.ReservationId.HasValue)
        {
            await LoadAsync();
            ModelState.AddModelError(string.Empty, "Please select a reservation for dine-in orders, or choose Takeaway.");
            return Page();
        }

        var userId = _userManager.GetUserId(User)!;
        var (success, message, order) = await _orderService.PlaceOrderAsync(
            userId, cart, Checkout.OrderType, Checkout.ReservationId, Checkout.Notes, Checkout.PaymentMethod);

        if (!success)
        {
            await LoadAsync();
            ModelState.AddModelError(string.Empty, message);
            return Page();
        }

        HttpContext.Session.Remove(CartSessionKey);
        TempData["StatusMessage"] = "Your order has been placed!";
        return RedirectToPage("/Orders/Details", new { id = order!.Id });
    }

    private async Task LoadAsync()
    {
        CartItems = GetCart();
        EstimatedTax = await _orderService.CalculateTaxAsync(SubTotal);

        var userId = _userManager.GetUserId(User);
        if (userId != null)
        {
            UpcomingReservations = await _context.Reservations
                .Include(r => r.Table)
                .Where(r => r.CustomerId == userId && r.ReservationDateTime >= DateTime.Now
                            && (r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.Pending))
                .OrderBy(r => r.ReservationDateTime)
                .ToListAsync();
        }
    }

    private List<CartItemVM> GetCart()
    {
        var json = HttpContext.Session.GetString(CartSessionKey);
        if (string.IsNullOrEmpty(json)) return new List<CartItemVM>();
        return JsonSerializer.Deserialize<List<CartItemVM>>(json) ?? new List<CartItemVM>();
    }

    private void SaveCart(List<CartItemVM> cart)
    {
        HttpContext.Session.SetString(CartSessionKey, JsonSerializer.Serialize(cart));
    }
}
