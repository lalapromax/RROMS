using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Admin;

public class AnnouncementsModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly INotificationService _notificationService;

    public AnnouncementsModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager, INotificationService notificationService)
    {
        _context = context;
        _userManager = userManager;
        _notificationService = notificationService;
    }

    public List<Announcement> Announcements { get; set; } = new();

    public async Task OnGetAsync()
    {
        Announcements = await _context.Announcements.OrderByDescending(a => a.CreatedAt).ToListAsync();
    }

    public async Task<IActionResult> OnPostAddAsync(string title, string content, DateTime? expiresAt, bool notifyCustomers)
    {
        var adminId = _userManager.GetUserId(User);
        var announcement = new Announcement
        {
            Title = title,
            Content = content,
            ExpiresAt = expiresAt,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedByAdminId = adminId
        };
        _context.Announcements.Add(announcement);
        await _context.SaveChangesAsync();

        if (notifyCustomers)
            await _notificationService.NotifyAllCustomersAsync(title, content, NotificationType.System);

        TempData["StatusMessage"] = "Announcement published.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        var a = await _context.Announcements.FindAsync(id);
        if (a != null)
        {
            a.IsActive = !a.IsActive;
            await _context.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var a = await _context.Announcements.FindAsync(id);
        if (a != null)
        {
            _context.Announcements.Remove(a);
            await _context.SaveChangesAsync();
            TempData["StatusMessage"] = "Announcement removed.";
        }
        return RedirectToPage();
    }
}
