using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Presentation;
using RROMS.ViewModels;

namespace RROMS.Services;

/// One cart line as it prices out right now, from the database (never from what the browser remembers).
public class QuotedLine
{
    public CartItemVM Cart { get; set; } = null!;

    /// Per item, per glass or bottle, or per guest for a set menu.
    public decimal UnitPrice { get; set; }
    public decimal Gross => UnitPrice * Cart.Quantity;

    /// What offers or a promo code took off this line.
    public decimal Discount { get; set; }
    public int? PromotionId { get; set; }

    /// Why the line cannot be ordered (sold out, set menu changed...). Null when it is fine.
    public string? Problem { get; set; }
    public bool Ok => Problem == null;

    public MenuItem? Item { get; set; }
    public SetMenuQuote? Set { get; set; }
}

/// Everything the cart page shows and checkout charges, worked out in exactly one place.
public class CartQuote
{
    public List<QuotedLine> Lines { get; } = new();

    public decimal SubTotal { get; set; }
    public decimal Discount { get; set; }

    public string? CodeEntered { get; set; }
    public bool CodeApplied { get; set; }
    public string? CodeMessage { get; set; }
    public int? CodePromotionId { get; set; }
    public decimal CodeDiscount { get; set; }

    public decimal ServiceChargePercent { get; set; }
    public decimal ServiceCharge { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public int PointsEstimate { get; set; }

    public bool HasProblem => Lines.Any(l => !l.Ok);
    public string? FirstProblem => Lines.FirstOrDefault(l => !l.Ok)?.Problem;

    internal PromotionCalculationResult? AutoResult { get; set; }
}

public interface IOrderService
{
    /// Prices a cart: lines, offers, an optional promo code, service charge (dine-in), tax and the points it will earn.
    Task<CartQuote> QuoteAsync(string customerId, List<CartItemVM> items, OrderType type, string? promoCode);

    Task<(bool Success, string Message, Order? Order)> PlaceOrderAsync(
        string customerId, List<CartItemVM> items, OrderType type, int? reservationId, string? notes, PaymentMethod method,
        string? promoCode = null, string? stripePaymentIntentId = null);

    /// 
    /// Creates a Stripe PaymentIntent for the customer's current cart, priced the exact
    /// same way PlaceOrderAsync prices it, so the embedded Payment Element the customer
    /// confirms is always for the real total
    Task<(bool Success, string Message, string? ClientSecret, string? PaymentIntentId)> CreateOnlinePaymentIntentAsync(
        string customerId, List<CartItemVM> items, OrderType type, string? promoCode, bool onlineBanking);

    /// 
    /// Authoritative, signature-verified Stripe webhook handling (brief 6.C): reconciles
    /// Payment/Order status from Stripe's own event, never from a frontend callback.
    /// 
    Task HandleStripeEventAsync(Stripe.Event stripeEvent);

    Task<(bool Success, string Message)> UpdateOrderStatusAsync(int orderId, OrderStatus newStatus, string staffId);
    Task<(bool Success, string Message)> CancelOrderAsync(int orderId, string requestingUserId, bool isStaffOrAdmin);
    Task<(bool Success, string Message)> RecordPaymentAsync(int orderId, PaymentMethod method, string staffId, string? transactionRef);
    Task<List<Order>> GetCustomerOrdersAsync(string customerId);
    Task<Order?> GetByIdAsync(int id);
    Task<List<Order>> GetActiveOrdersAsync();
}

public class OrderService : IOrderService
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly IPromotionService _promotionService;
    private readonly IStripePaymentService _stripeService;
    private readonly IRewardService _rewardService;
    private readonly ISetMenuService _setMenus;

    public OrderService(ApplicationDbContext context, INotificationService notificationService, IPromotionService promotionService,
        IStripePaymentService stripeService, IRewardService rewardService, ISetMenuService setMenus)
    {
        _context = context;
        _notificationService = notificationService;
        _promotionService = promotionService;
        _stripeService = stripeService;
        _rewardService = rewardService;
        _setMenus = setMenus;
    }

    private static bool IsOnlineMethod(PaymentMethod m) => m is PaymentMethod.OnlinePayment or PaymentMethod.OnlineBanking;

    /// Splits an amount across weights, in cents, so the parts always add up to exactly the amount.
    private static decimal[] Spread(decimal amount, IReadOnlyList<decimal> weights)
    {
        var result = new decimal[weights.Count];
        var total = weights.Sum();
        if (weights.Count == 0 || total <= 0 || amount <= 0) return result;

        var last = -1;
        for (var i = 0; i < weights.Count; i++) if (weights[i] > 0) last = i;

        decimal given = 0m;
        for (var i = 0; i < weights.Count; i++)
        {
            if (weights[i] <= 0) continue;
            var part = i == last ? amount - given : Math.Round(amount * weights[i] / total, 2);
            part = Math.Min(part, weights[i]);       // a line is never discounted below nothing
            result[i] = part;
            given += part;
        }
        return result;
    }

    public async Task<CartQuote> QuoteAsync(string customerId, List<CartItemVM> items, OrderType type, string? promoCode)
    {
        var quote = new CartQuote();
        if (items == null || items.Count == 0) return quote;

        var dishIds = items.Where(i => i.Kind != CartKind.SetMenu).Select(i => i.MenuItemId).Distinct().ToList();
        var menuItems = await _context.MenuItems.AsNoTracking().Where(m => dishIds.Contains(m.Id)).ToListAsync();
        var email = await _context.Users.Where(u => u.Id == customerId).Select(u => u.Email).FirstOrDefaultAsync();

        foreach (var cart in items)
        {
            var line = new QuotedLine { Cart = cart };
            quote.Lines.Add(line);

            if (cart.Quantity < 1 || cart.Quantity > 50)
            {
                line.Problem = $"{cart.Name}: choose between 1 and 50.";
                continue;
            }

            if (cart.Kind == CartKind.SetMenu)
            {
                if (cart.SetMenuId is not int setId)
                {
                    line.Problem = $"{cart.Name} is no longer on offer.";
                    continue;
                }
                var q = await _setMenus.QuoteAsync(setId, cart.Picks.Select(p => new SetMenuPick(p.CourseId, p.MenuItemId)));
                if (!q.Valid) { line.Problem = $"{cart.Name}: {q.Message}"; continue; }
                line.Set = q;
                line.UnitPrice = q.PerGuest;
                continue;
            }

            var m = menuItems.FirstOrDefault(x => x.Id == cart.MenuItemId);
            if (m == null || !m.IsAvailable) { line.Problem = $"{cart.Name} is not available right now."; continue; }
            if (cart.Kind == CartKind.WineGlass && !(m.GlassPrice > 0)) { line.Problem = $"{m.Name} is sold by the bottle only."; continue; }
            line.Item = m;
            line.UnitPrice = cart.Kind == CartKind.WineGlass ? m.GlassPrice!.Value : m.Price;
        }

        var ok = quote.Lines.Where(l => l.Ok).ToList();
        quote.SubTotal = ok.Sum(l => l.Gross);

        // Offers that apply by themselves: dishes and bottles only. Glasses and set menus are already priced as composed.
        var offerLines = ok.Where(l => l.Item != null && l.Cart.Kind != CartKind.WineGlass).ToList();
        var auto = await _promotionService.CalculateOrderDiscountsAsync(
            customerId, email,
            offerLines.GroupBy(l => l.Item!.Id).Select(g => (g.First().Item!, g.Sum(l => l.Cart.Quantity))),
            DateTime.UtcNow, quote.SubTotal);
        foreach (var d in auto.Lines)
        {
            var group = offerLines.Where(l => l.Item!.Id == d.MenuItemId).ToList();
            var parts = Spread(d.DiscountAmount, group.Select(l => l.Gross).ToList());
            for (var i = 0; i < group.Count; i++) { group[i].Discount = parts[i]; group[i].PromotionId = d.PromotionId; }
        }
        var autoTotal = ok.Sum(l => l.Discount);
        quote.AutoResult = auto;

        // A promo code stands in for the automatic offers when it saves more; it never piles on top of them.
        if (!string.IsNullOrWhiteSpace(promoCode))
        {
            quote.CodeEntered = promoCode.Trim();
            var check = await _promotionService.CheckCodeAsync(promoCode, customerId, email, quote.SubTotal,
                offerLines.Select(l => (l.Item!, l.Gross)), DateTime.UtcNow);
            if (!check.Ok)
            {
                quote.CodeMessage = check.Message;
            }
            else if (check.Discount > autoTotal)
            {
                var promo = check.Promotion!;
                foreach (var l in ok) { l.Discount = 0m; l.PromotionId = null; }
                var wholeBill = promo.MenuItemId == null && promo.MenuCategoryId == null;
                var weights = ok.Select(l =>
                    wholeBill ? l.Gross
                    : l.Item != null && (promo.MenuItemId == l.Item.Id || (promo.MenuItemId == null && promo.MenuCategoryId == l.Item.MenuCategoryId)) ? l.Gross
                    : 0m).ToList();
                var parts = Spread(check.Discount, weights);
                for (var i = 0; i < ok.Count; i++) { ok[i].Discount = parts[i]; if (parts[i] > 0) ok[i].PromotionId = promo.Id; }

                quote.CodeApplied = true;
                quote.CodePromotionId = promo.Id;
                quote.CodeDiscount = check.Discount;
                quote.CodeMessage = check.Message;
                quote.AutoResult = null;
            }
            else
            {
                quote.CodeMessage = $"{check.Promotion!.Code} would take off {Ui.Rm(check.Discount)}, but the offers already in your cart save {Ui.Rm(autoTotal)}, so those stay.";
            }
        }

        quote.Discount = ok.Sum(l => l.Discount);

        var setting = await _context.RestaurantSettings.AsNoTracking().FirstOrDefaultAsync();
        quote.ServiceChargePercent = type == OrderType.DineIn ? setting?.ServiceChargePercent ?? 0m : 0m;
        quote.ServiceCharge = Math.Round(quote.SubTotal * quote.ServiceChargePercent / 100m, 2);
        quote.TaxPercent = setting?.TaxRatePercent ?? 0m;
        // Tax is charged on the food and drink after discounts, plus the service charge.
        quote.Tax = Math.Round((quote.SubTotal - quote.Discount + quote.ServiceCharge) * quote.TaxPercent / 100m, 2);
        quote.Total = quote.SubTotal - quote.Discount + quote.ServiceCharge + quote.Tax;
        quote.PointsEstimate = (int)Math.Floor(quote.Total) * _rewardService.PointsPerCurrencyUnit;
        return quote;
    }

    public async Task<(bool Success, string Message, Order? Order)> PlaceOrderAsync(
        string customerId, List<CartItemVM> items, OrderType type, int? reservationId, string? notes, PaymentMethod method,
        string? promoCode = null, string? stripePaymentIntentId = null)
    {
        var quote = await QuoteAsync(customerId, items, type, promoCode);
        if (quote.Lines.Count == 0)
            return (false, "Your cart is empty.", null);
        if (quote.HasProblem)
            return (false, quote.FirstProblem + " Please take it out of your cart and try again.", null);

        // Online payment must already have been confirmed through Stripe before the order is placed, and is
        // re-verified here against Stripe's own API (never the browser's say-so): brief 6.C, "do not treat a
        // successful frontend callback alone as proof".
        Stripe.PaymentIntent? verifiedIntent = null;
        if (IsOnlineMethod(method))
        {
            if (!_stripeService.IsConfigured)
                return (false, "Online payment isn't available right now. Please choose to pay at the restaurant instead.", null);

            if (string.IsNullOrWhiteSpace(stripePaymentIntentId))
                return (false, "Online payment was not completed. Please try again.", null);

            try
            {
                verifiedIntent = await _stripeService.GetPaymentIntentAsync(stripePaymentIntentId);
            }
            catch (Stripe.StripeException)
            {
                return (false, "We couldn't verify that payment with Stripe. Please try again.", null);
            }

            if (verifiedIntent.Status != "succeeded")
                return (false, $"Payment has not completed yet (status: {verifiedIntent.Status}). Please try again.", null);

            // The amount Stripe actually charged must match what this cart prices out to right now. If the cart
            // changed between creating the PaymentIntent and placing the order, refuse rather than confirm the wrong amount.
            var expectedMinorUnits = (long)Math.Round(quote.Total * 100m, MidpointRounding.AwayFromZero);
            if (verifiedIntent.Amount != expectedMinorUnits)
                return (false, "Your cart changed since payment was taken. Please contact the restaurant before ordering again.", null);
        }

        int? tableId = null;
        Reservation? reservation = null;
        if (type == OrderType.DineIn)
        {
            if (!reservationId.HasValue)
                return (false, "Choose the reservation this order is for.", null);
            reservation = await _context.Reservations.FirstOrDefaultAsync(r => r.Id == reservationId.Value && r.CustomerId == customerId);
            if (reservation == null)
                return (false, "The selected reservation could not be found.", null);
            if (reservation.Status is ReservationStatus.Cancelled or ReservationStatus.Completed or ReservationStatus.NoShow)
                return (false, "That reservation is no longer active.", null);
            tableId = reservation.TableId;
        }

        var order = new Order
        {
            CustomerId = customerId,
            ReservationId = reservation?.Id,
            TableId = tableId,
            OrderType = type,
            Status = OrderStatus.Placed,
            OrderDate = DateTime.UtcNow,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };

        // Every figure below comes from the one quote that was just verified, never recomputed, so the order cannot
        // end up charged differently from what was checked.
        var group = 0;
        foreach (var line in quote.Lines)
        {
            var qty = line.Cart.Quantity;
            var instructions = line.Cart.SpecialInstructions;

            if (line.Set != null)
            {
                group++;
                var set = line.Set;
                // One order line per dish the guest chose, so the kitchen sees every dish. The set price (plus any
                // supplements) is shared out in proportion to what each dish is worth, so totals and reports still add up.
                var worth = set.Chosen.Select(c => (c.Dish.MenuItem?.Price ?? 0m) + c.Dish.Supplement).ToList();
                if (worth.Sum() <= 0) worth = worth.Select(_ => 1m).ToList();
                var shares = Spread(set.PerGuest, worth);
                var discounts = Spread(line.Discount, shares.Select(sh => sh * qty).ToList());
                for (var i = 0; i < set.Chosen.Count; i++)
                {
                    var (course, dish) = set.Chosen[i];
                    order.OrderItems.Add(new OrderItem
                    {
                        MenuItemId = dish.MenuItemId,
                        ItemNameSnapshot = Trim(dish.MenuItem?.Name ?? line.Cart.Name, 100),
                        Quantity = qty,
                        UnitPrice = shares[i],
                        Subtotal = shares[i] * qty - discounts[i],
                        DiscountAmount = discounts[i],
                        PromotionId = discounts[i] > 0 ? line.PromotionId : null,
                        SpecialInstructions = Trim(instructions, 200),
                        SetMenuId = line.Cart.SetMenuId,
                        SetMenuName = Trim(line.Cart.Name, 100),
                        SetMenuGroup = group,
                        CourseName = Trim(course.Name, 40)
                    });
                }
                continue;
            }

            order.OrderItems.Add(new OrderItem
            {
                MenuItemId = line.Item!.Id,
                ItemNameSnapshot = Trim(line.Item.Name, 100),
                Quantity = qty,
                UnitPrice = line.UnitPrice,
                Subtotal = line.Gross - line.Discount,
                DiscountAmount = line.Discount,
                PromotionId = line.Discount > 0 ? line.PromotionId : null,
                SpecialInstructions = Trim(instructions, 200),
                Serving = line.Cart.Kind switch { CartKind.WineGlass => "Glass", CartKind.WineBottle => "Bottle", _ => null }
            });
        }

        order.SubTotal = quote.SubTotal;
        order.DiscountAmount = quote.Discount;
        order.ServiceChargeAmount = quote.ServiceCharge;
        order.TaxAmount = quote.Tax;
        order.TotalAmount = quote.Total;

        // Paying at the restaurant is settled later by staff in Billing. Online payment was already taken through
        // Stripe and verified above, so it is recorded as paid straight away with the PaymentIntent id as the
        // reference (it is also how the webhook finds this payment again).
        var payment = new Payment
        {
            Amount = order.TotalAmount,
            Method = method,
            CreatedAt = DateTime.UtcNow
        };

        if (IsOnlineMethod(method))
        {
            payment.Status = PaymentStatus.Paid;
            payment.PaidAt = DateTime.UtcNow;
            payment.TransactionReference = verifiedIntent!.Id;
        }
        else
        {
            payment.Status = PaymentStatus.Pending;
        }

        order.Payment = payment;

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        // Now the order has a real id: record which offers or code it used, which is what the use limits count.
        if (quote.CodeApplied && quote.CodePromotionId.HasValue)
            await _promotionService.RecordCodeRedemptionAsync(quote.CodePromotionId.Value, order.Id, customerId, quote.CodeDiscount);
        else if (quote.AutoResult != null)
            await _promotionService.RecordRedemptionsAsync(quote.AutoResult, order.Id, customerId);

        await _notificationService.NotifyAsync(
            customerId,
            "Order placed",
            $"Order #{order.Id} for {order.OrderItems.Sum(i => i.Quantity)} item(s) totalling {Ui.Rm2(order.TotalAmount)} has been placed.",
            NotificationType.Order,
            "/Orders/Details?id=" + order.Id);

        if (payment.Status == PaymentStatus.Paid)
        {
            await _notificationService.NotifyAsync(
                customerId,
                "Payment received",
                $"We've received your payment of {Ui.Rm2(payment.Amount)} for order #{order.Id}. Reference: {payment.TransactionReference}.",
                NotificationType.Payment,
                "/Orders/Details?id=" + order.Id,
                sendEmail: false);
        }

        return (true, "Order placed successfully.", order);
    }

    private static string Trim(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : (value.Trim().Length > max ? value.Trim()[..max] : value.Trim());

    public async Task<(bool Success, string Message, string? ClientSecret, string? PaymentIntentId)> CreateOnlinePaymentIntentAsync(
        string customerId, List<CartItemVM> items, OrderType type, string? promoCode, bool onlineBanking)
    {
        if (!_stripeService.IsConfigured)
            return (false, "Online payment isn't set up yet. Please choose to pay at the restaurant.", null, null);

        var quote = await QuoteAsync(customerId, items, type, promoCode);
        if (quote.Lines.Count == 0)
            return (false, "Your cart is empty.", null, null);
        if (quote.HasProblem)
            return (false, quote.FirstProblem + " Please take it out of your cart first.", null, null);

        var (clientSecret, paymentIntentId) = await _stripeService.CreatePaymentIntentAsync(quote.Total, customerId, onlineBanking);
        return (true, "Payment intent created.", clientSecret, paymentIntentId);
    }

    public async Task HandleStripeEventAsync(Stripe.Event stripeEvent)
    {
        if (stripeEvent.Data.Object is not Stripe.PaymentIntent intent)
            return;

        // Looked up by the id Stripe generated, never by anything a client sent - this
        // is what makes the handler safe to expose with no sign-in required.
        var payment = await _context.Payments
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.TransactionReference == intent.Id);

        // No matching payment yet (e.g. the event arrived before OnPostCheckoutAsync
        // finished creating the order) - nothing to reconcile. The synchronous check in
        // PlaceOrderAsync is what actually gates order placement; this handler only
        // keeps a placed order's status in sync with later Stripe events.
        if (payment == null)
            return;

        switch (stripeEvent.Type)
        {
            case "payment_intent.succeeded":
                if (payment.Status != PaymentStatus.Paid)
                {
                    payment.Status = PaymentStatus.Paid;
                    payment.PaidAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();

                    if (payment.Order != null)
                        await _rewardService.TryAwardForOrderAsync(payment.Order.Id);
                }
                break;

            case "payment_intent.payment_failed":
            case "payment_intent.canceled":
                // Idempotent: a duplicate delivery of the same event, or one arriving
                // after the payment was already confirmed another way, changes nothing.
                if (payment.Status == PaymentStatus.Pending)
                {
                    payment.Status = PaymentStatus.Failed;
                    await _context.SaveChangesAsync();

                    if (payment.Order != null)
                    {
                        await _notificationService.NotifyAsync(
                            payment.Order.CustomerId,
                            "Payment failed",
                            $"Online payment for order #{payment.Order.Id} did not go through. Please contact staff to arrange payment.",
                            NotificationType.Payment,
                            "/Orders/Details?id=" + payment.Order.Id,
                            sendEmail: false);
                    }
                }
                break;
        }
    }

    public async Task<(bool Success, string Message)> UpdateOrderStatusAsync(int orderId, OrderStatus newStatus, string staffId)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null)
            return (false, "Order not found.");
        if (order.Status is OrderStatus.Completed or OrderStatus.Cancelled)
            return (false, "This order is already closed.");

        order.Status = newStatus;
        order.HandledByStaffId = staffId;
        await _context.SaveChangesAsync();

        await _notificationService.NotifyAsync(
            order.CustomerId,
            "Order update",
            $"Order #{order.Id} is now: {newStatus}.",
            NotificationType.Order,
            "/Orders/Details?id=" + order.Id,
            sendEmail: false);

        // Rewards are only earned by completed AND paid orders; the reward service checks both
        // and ignores repeats, so it is safe to ask every time an order reaches Completed.
        if (newStatus == OrderStatus.Completed)
            await _rewardService.TryAwardForOrderAsync(order.Id);

        return (true, "Order status updated.");
    }

    public async Task<(bool Success, string Message)> CancelOrderAsync(int orderId, string requestingUserId, bool isStaffOrAdmin)
    {
        var order = await _context.Orders.Include(o => o.Payment).FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null)
            return (false, "Order not found.");
        if (!isStaffOrAdmin && order.CustomerId != requestingUserId)
            return (false, "You can only cancel your own orders.");
        if (order.Status is OrderStatus.Completed or OrderStatus.Cancelled)
            return (false, "This order can no longer be cancelled.");
        if (!isStaffOrAdmin && order.Status != OrderStatus.Placed)
            return (false, "The kitchen has already started this order - please contact staff to cancel it.");

        // A payment made online is returned through Stripe BEFORE the order is closed. If Stripe refuses, nothing changes and
        // the guest is told to call, rather than the order saying "refunded" while the money has not moved.
        var refunded = false;
        var paid = order.Payment is { Status: PaymentStatus.Paid };
        if (paid && IsOnlineMethod(order.Payment!.Method) && !string.IsNullOrWhiteSpace(order.Payment.TransactionReference))
        {
            var (ok, why) = await _stripeService.RefundAsync(order.Payment.TransactionReference!);
            if (!ok)
                return (false, "We could not return your payment automatically (" + why.TrimEnd('.') + "). Please call the restaurant and we will sort it out.");
            refunded = true;
        }

        order.Status = OrderStatus.Cancelled;
        if (paid) order.Payment!.Status = PaymentStatus.Refunded;

        await _context.SaveChangesAsync();

        await _notificationService.NotifyAsync(
            order.CustomerId,
            "Order cancelled",
            refunded ? $"Order #{order.Id} has been cancelled. Your payment of {Ui.Rm2(order.Payment!.Amount)} is on its way back to you."
                     : $"Order #{order.Id} has been cancelled.",
            NotificationType.Order,
            "/Orders/Details?id=" + order.Id);

        return (true, refunded ? "Order cancelled. Your payment is on its way back to you." : "Order cancelled.");
    }

    public async Task<(bool Success, string Message)> RecordPaymentAsync(int orderId, PaymentMethod method, string staffId, string? transactionRef)
    {
        var order = await _context.Orders.Include(o => o.Payment).FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null)
            return (false, "Order not found.");

        if (order.Payment == null)
        {
            order.Payment = new Payment { OrderId = order.Id, Amount = order.TotalAmount };
        }

        order.Payment.Method = method;
        order.Payment.Status = PaymentStatus.Paid;
        order.Payment.PaidAt = DateTime.UtcNow;
        order.Payment.TransactionReference = transactionRef ?? "TXN-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        order.HandledByStaffId = staffId;

        await _context.SaveChangesAsync();

        await _notificationService.NotifyAsync(
            order.CustomerId,
            "Payment confirmed",
            $"Payment of {Ui.Rm2(order.Payment.Amount)} for order #{order.Id} has been recorded. Thank you!",
            NotificationType.Payment,
            "/Orders/Details?id=" + order.Id,
            sendEmail: false);

        // The order may already be Completed while payment was still outstanding (e.g. a
        // cash/card order settled at the till afterwards) - that is the moment it becomes eligible.
        await _rewardService.TryAwardForOrderAsync(order.Id);

        return (true, "Payment recorded.");
    }

    public async Task<List<Order>> GetCustomerOrdersAsync(string customerId)
    {
        return await _context.Orders
            .Include(o => o.OrderItems).ThenInclude(i => i.MenuItem).ThenInclude(m => m!.MenuCategory)
            .Include(o => o.Payment)
            .Include(o => o.Table)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();
    }

    public async Task<Order?> GetByIdAsync(int id)
    {
        return await _context.Orders
            .Include(o => o.OrderItems).ThenInclude(i => i.MenuItem).ThenInclude(m => m!.MenuCategory)
            .Include(o => o.Payment)
            .Include(o => o.Table)
            .Include(o => o.Customer)
            .Include(o => o.Reservation)
            .Include(o => o.PromotionRedemptions).ThenInclude(r => r.Promotion)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<List<Order>> GetActiveOrdersAsync()
    {
        return await _context.Orders
            .Include(o => o.OrderItems)
            .Include(o => o.Payment)
            .Include(o => o.Customer)
            .Include(o => o.Table)
            .Include(o => o.Reservation) // so staff can see which reservation this order belongs to (brief 5.D)
            .Where(o => o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled)
            .OrderBy(o => o.OrderDate)
            .ToListAsync();
    }
}
