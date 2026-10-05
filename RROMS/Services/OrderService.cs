using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.ViewModels;

namespace RROMS.Services;

public interface IOrderService
{
    Task<(bool Success, string Message, Order? Order)> PlaceOrderAsync(
        string customerId, List<CartItemVM> items, OrderType type, int? reservationId, string? notes, PaymentMethod method,
        string? cardNumberLast4 = null);
    Task<(bool Success, string Message)> UpdateOrderStatusAsync(int orderId, OrderStatus newStatus, string staffId);
    Task<(bool Success, string Message)> CancelOrderAsync(int orderId, string requestingUserId, bool isStaffOrAdmin);
    Task<(bool Success, string Message)> RecordPaymentAsync(int orderId, PaymentMethod method, string staffId, string? transactionRef);
    Task<List<Order>> GetCustomerOrdersAsync(string customerId);
    Task<Order?> GetByIdAsync(int id);
    Task<List<Order>> GetActiveOrdersAsync();
    Task<decimal> CalculateTaxAsync(decimal subtotal);
}

public class OrderService : IOrderService
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notificationService;

    public OrderService(ApplicationDbContext context, INotificationService notificationService)
    {
        _context = context;
        _notificationService = notificationService;
    }

    public async Task<decimal> CalculateTaxAsync(decimal subtotal)
    {
        var setting = await _context.RestaurantSettings.FirstOrDefaultAsync();
        var rate = setting?.TaxRatePercent ?? 0m;
        return Math.Round(subtotal * (rate / 100m), 2);
    }

    public async Task<(bool Success, string Message, Order? Order)> PlaceOrderAsync(
        string customerId, List<CartItemVM> items, OrderType type, int? reservationId, string? notes, PaymentMethod method,
        string? cardNumberLast4 = null)
    {
        if (items == null || items.Count == 0)
            return (false, "Your cart is empty.", null);

        // Re-price every line from the database so a stale cart can never under/over-charge.
        var menuItemIds = items.Select(i => i.MenuItemId).ToList();
        var menuItems = await _context.MenuItems.Where(m => menuItemIds.Contains(m.Id)).ToListAsync();

        int? tableId = null;
        Reservation? reservation = null;
        if (type == OrderType.DineIn && reservationId.HasValue)
        {
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
            Notes = notes
        };

        decimal subtotal = 0m;
        foreach (var cartLine in items)
        {
            var menuItem = menuItems.FirstOrDefault(m => m.Id == cartLine.MenuItemId);
            if (menuItem == null || !menuItem.IsAvailable) continue;

            var lineSubtotal = menuItem.Price * cartLine.Quantity;
            subtotal += lineSubtotal;

            order.OrderItems.Add(new OrderItem
            {
                MenuItemId = menuItem.Id,
                ItemNameSnapshot = menuItem.Name,
                Quantity = cartLine.Quantity,
                UnitPrice = menuItem.Price,
                Subtotal = lineSubtotal,
                SpecialInstructions = cartLine.SpecialInstructions
            });
        }

        if (order.OrderItems.Count == 0)
            return (false, "None of the items in your cart are currently available.", null);

        var tax = await CalculateTaxAsync(subtotal);
        order.SubTotal = subtotal;
        order.TaxAmount = tax;
        order.TotalAmount = subtotal + tax;

        // Simulated payment processing. Cash is settled later by staff; card/online is "processed"
        // immediately here since there is no live payment gateway wired into this coursework build.
        var payment = new Payment
        {
            Amount = order.TotalAmount,
            Method = method,
            CreatedAt = DateTime.UtcNow
        };

        if (method == PaymentMethod.Cash)
        {
            payment.Status = PaymentStatus.Pending;
        }
        else
        {
            payment.Status = PaymentStatus.Paid;
            payment.PaidAt = DateTime.UtcNow;
            payment.TransactionReference = "TXN-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        }

        order.Payment = payment;

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        await _notificationService.NotifyAsync(
            customerId,
            "Order placed",
            $"Order #{order.Id} for {order.OrderItems.Sum(i => i.Quantity)} item(s) totalling {order.TotalAmount:C} has been placed.",
            NotificationType.Order,
            "/Orders/Details?id=" + order.Id);

        if (payment.Status == PaymentStatus.Paid)
        {
            await _notificationService.NotifyAsync(
                customerId,
                "Payment received",
                $"We've received your payment of {payment.Amount:C} for order #{order.Id}. Reference: {payment.TransactionReference}.",
                NotificationType.Payment,
                "/Orders/Details?id=" + order.Id,
                sendEmail: false);
        }

        return (true, "Order placed successfully.", order);
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

        order.Status = OrderStatus.Cancelled;
        if (order.Payment != null && order.Payment.Status == PaymentStatus.Paid)
            order.Payment.Status = PaymentStatus.Refunded;

        await _context.SaveChangesAsync();

        await _notificationService.NotifyAsync(
            order.CustomerId,
            "Order cancelled",
            $"Order #{order.Id} has been cancelled.",
            NotificationType.Order,
            "/Orders/Details?id=" + order.Id);

        return (true, "Order cancelled.");
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
            $"Payment of {order.Payment.Amount:C} for order #{order.Id} has been recorded. Thank you!",
            NotificationType.Payment,
            "/Orders/Details?id=" + order.Id,
            sendEmail: false);

        return (true, "Payment recorded.");
    }

    public async Task<List<Order>> GetCustomerOrdersAsync(string customerId)
    {
        return await _context.Orders
            .Include(o => o.OrderItems)
            .Include(o => o.Payment)
            .Include(o => o.Table)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();
    }

    public async Task<Order?> GetByIdAsync(int id)
    {
        return await _context.Orders
            .Include(o => o.OrderItems)
            .Include(o => o.Payment)
            .Include(o => o.Table)
            .Include(o => o.Customer)
            .Include(o => o.Reservation)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<List<Order>> GetActiveOrdersAsync()
    {
        return await _context.Orders
            .Include(o => o.OrderItems)
            .Include(o => o.Payment)
            .Include(o => o.Customer)
            .Include(o => o.Table)
            .Where(o => o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled)
            .OrderBy(o => o.OrderDate)
            .ToListAsync();
    }
}
