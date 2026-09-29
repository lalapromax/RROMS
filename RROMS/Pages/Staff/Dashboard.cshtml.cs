using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;

namespace RROMS.Pages.Staff;

public class DashboardModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public DashboardModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public List<Reservation> TodayReservations { get; set; } = new();
    public List<Order> ActiveOrders { get; set; } = new();
    public int PendingPayments { get; set; }

    public async Task OnGetAsync()
    {
        var start = DateTime.Today;
        var end = start.AddDays(1);

        TodayReservations = await _context.Reservations
            .Include(r => r.Customer)
            .Include(r => r.Table)
            .Where(r => r.ReservationDateTime >= start && r.ReservationDateTime < end)
            .OrderBy(r => r.ReservationDateTime)
            .ToListAsync();

        ActiveOrders = await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.OrderItems)
            .Include(o => o.Payment)
            .Where(o => o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled)
            .OrderBy(o => o.OrderDate)
            .ToListAsync();

        PendingPayments = await _context.Payments.CountAsync(p => p.Status == PaymentStatus.Pending);
    }
}
