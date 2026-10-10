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
    private readonly IOrderService _orders;
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IStripePaymentService _stripe;

    public CartModel(IOrderService orders, ApplicationDbContext context, UserManager<ApplicationUser> userManager, IStripePaymentService stripe)
    {
        _orders = orders;
        _context = context;
        _userManager = userManager;
        _stripe = stripe;
    }

    /// Whether real Stripe keys are set. Without them the two online choices are shown but switched off (brief 6.C:
    /// never pretend live payments work when no credentials are configured).
    public bool StripeEnabled => _stripe.IsConfigured;
    public string StripePublishableKey => _stripe.PublishableKey;

    public List<CartItemVM> CartItems { get; private set; } = new();
    public CartQuote Quote { get; private set; } = new();
    public List<Reservation> Reservations { get; private set; } = new();
    public Reservation? SelectedReservation { get; private set; }
    public OrderType OrderType => SelectedReservation != null ? OrderType.DineIn : OrderType.Takeaway;

    [BindProperty]
    public CheckoutVM Checkout { get; set; } = new();

    public async Task OnGetAsync(int? reservationId)
    {
        Checkout.ReservationId = reservationId;
        Checkout.PaymentMethod = StripeEnabled ? PaymentMethod.OnlinePayment : PaymentMethod.Cash;
        await LoadAsync();
    }

    /// The +, - and Remove buttons. The quantity posted is the one wanted, so a repeated post changes nothing more.
    public async Task<IActionResult> OnPostSetQtyAsync(string key, int qty)
    {
        var cart = CartSession.Load(HttpContext.Session);
        var line = cart.FirstOrDefault(c => c.Key == key);
        if (line != null)
        {
            if (qty <= 0) cart.Remove(line);
            else line.Quantity = Math.Min(50, qty);
            CartSession.Save(HttpContext.Session, cart);
        }
        return await RedrawAsync();
    }

    public async Task<IActionResult> OnPostRemoveAsync(string key)
    {
        var cart = CartSession.Load(HttpContext.Session);
        cart.RemoveAll(c => c.Key == key);
        CartSession.Save(HttpContext.Session, cart);
        return await RedrawAsync();
    }

    /// "Apply" next to the promo code: redraws the cart with the code priced in, keeping everything else typed so far.
    public async Task<IActionResult> OnPostApplyCodeAsync() => await RedrawAsync();

    /// Redraws the page without a redirect, so notes, payment choice and code typed so far are kept.
    private async Task<IActionResult> RedrawAsync()
    {
        ModelState.Clear();
        await LoadAsync();
        return Page();
    }

    /// 
    /// Called by the page's script when an online payment choice is picked, so the Stripe form has a client secret to mount.
    /// The amount comes from the same pricing checkout uses, never from the browser.
    /// 
    public async Task<IActionResult> OnPostCreatePaymentIntentAsync()
    {
        await LoadAsync();
        var userId = _userManager.GetUserId(User)!;
        var (success, message, clientSecret, paymentIntentId) = await _orders.CreateOnlinePaymentIntentAsync(
            userId, CartItems, OrderType, Checkout.PromoCode, Checkout.PaymentMethod == PaymentMethod.OnlineBanking);

        if (!success)
            return new JsonResult(new { success = false, message }) { StatusCode = 400 };

        return new JsonResult(new { success = true, clientSecret, paymentIntentId, total = Quote.Total });
    }

    public async Task<IActionResult> OnPostCheckoutAsync()
    {
        await LoadAsync();
        if (CartItems.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Your cart is empty.");
            return Page();
        }
        if (Quote.HasProblem)
        {
            ModelState.AddModelError(string.Empty, Quote.FirstProblem + " Please take it out of your cart and try again.");
            return Page();
        }
        var online = Checkout.PaymentMethod is PaymentMethod.OnlinePayment or PaymentMethod.OnlineBanking;
        if (online && string.IsNullOrWhiteSpace(Checkout.StripePaymentIntentId))
        {
            ModelState.AddModelError(string.Empty, "Please finish the payment form before placing the order.");
            return Page();
        }

        var userId = _userManager.GetUserId(User)!;
        var (success, message, order) = await _orders.PlaceOrderAsync(
            userId, CartItems, OrderType, SelectedReservation?.Id, Checkout.Notes, Checkout.PaymentMethod,
            Checkout.PromoCode, Checkout.StripePaymentIntentId);

        if (!success)
        {
            ModelState.AddModelError(string.Empty, message);
            return Page();
        }

        CartSession.Clear(HttpContext.Session);
        TempData["StatusMessage"] = "Your order is with the kitchen.";
        return RedirectToPage("/Orders/Details", new { id = order!.Id });
    }

    private async Task LoadAsync()
    {
        CartItems = CartSession.Load(HttpContext.Session);
        var userId = _userManager.GetUserId(User)!;

        // Tables the guest can still order for: booked, not cancelled, and not over yet.
        var mine = await _context.Reservations.AsNoTracking()
            .Include(r => r.Table)
            .Where(r => r.CustomerId == userId
                        && (r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Seated))
            .OrderBy(r => r.ReservationDateTime)
            .ToListAsync();
        Reservations = mine.Where(r => r.ReservationEndDateTime >= DateTime.Now).ToList();

        SelectedReservation = Reservations.FirstOrDefault(r => r.Id == Checkout.ReservationId) ?? Reservations.FirstOrDefault();
        Checkout.ReservationId = SelectedReservation?.Id;

        if (!StripeEnabled && Checkout.PaymentMethod != PaymentMethod.Cash)
            Checkout.PaymentMethod = PaymentMethod.Cash;

        Quote = await _orders.QuoteAsync(userId, CartItems, OrderType, Checkout.PromoCode);
    }
}
