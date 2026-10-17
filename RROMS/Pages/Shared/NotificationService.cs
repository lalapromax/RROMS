using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;

namespace RROMS.Services;

public interface INotificationService
{
    Task NotifyAsync(string userId, string title, string message, NotificationType type, string? linkUrl = null, bool sendEmail = true);
    Task NotifyAllCustomersAsync(string title, string message, NotificationType type, string? linkUrl = null);

    /// In-app notification for a chosen set of users (already resolved by the caller). Returns how many were created.
    Task<int> NotifyUsersAsync(IEnumerable<string> userIds, string title, string message, NotificationType type, string? linkUrl = null);

    /// In-app notification for every active Administrator (e.g. a new reward claim is waiting).
    Task NotifyAdministratorsAsync(string title, string message, NotificationType type, string? linkUrl = null);
    Task<int> GetUnreadCountAsync(string userId);
    Task<List<Notification>> GetForUserAsync(string userId, int take = 20);
    Task MarkAllReadAsync(string userId);
    Task MarkReadAsync(int notificationId, string userId);

    /// Saves what a guest wants to hear about (Notices page and Profile share this).
    Task SavePreferencesAsync(string userId, bool announcements, bool rewards, bool offers, bool byEmail);
}

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailService _email;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ApplicationDbContext context, IEmailService email, ILogger<NotificationService> logger)
    {
        _context = context;
        _email = email;
        _logger = logger;
    }

    /// Whether this person asked to hear about this kind of notice. Booking, order and payment updates are always wanted.
    private static bool Wants(NotificationType type, bool announcements, bool rewards, bool offers) => type switch
    {
        NotificationType.Announcement => announcements,
        NotificationType.Reward => rewards,
        NotificationType.Promotion => offers,
        _ => true
    };

    public async Task NotifyAsync(string userId, string title, string message, NotificationType type, string? linkUrl = null, bool sendEmail = true)
    {
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null || user.IsSystemAccount || !Wants(type, user.NoticeAnnouncements, user.NoticeRewards, user.NoticeOffers))
            return;

        _context.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = Trim(title, 120),
            Message = Trim(message, 400),
            Type = type,
            LinkUrl = linkUrl,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        if (sendEmail && user.NoticeByEmail && user.Email != null)
            await TrySendEmailAsync(user.Email, title, message);
    }

    public async Task NotifyAllCustomersAsync(string title, string message, NotificationType type, string? linkUrl = null)
    {
        var customerIds = await _context.UserRoles
            .Join(_context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Where(x => x.Name == Roles.Customer)
            .Select(x => x.UserId)
            .ToListAsync();
        await NotifyUsersAsync(customerIds, title, message, type, linkUrl);
    }

    public async Task<int> NotifyUsersAsync(IEnumerable<string> userIds, string title, string message, NotificationType type, string? linkUrl = null)
    {
        var ids = userIds.Distinct().ToList();
        var prefs = await _context.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && (u.Email == null || !u.Email.EndsWith(".invalid")))
            .Select(u => new { u.Id, u.NoticeAnnouncements, u.NoticeRewards, u.NoticeOffers })
            .ToListAsync();

        var now = DateTime.UtcNow;
        var count = 0;
        foreach (var u in prefs.Where(u => Wants(type, u.NoticeAnnouncements, u.NoticeRewards, u.NoticeOffers)))
        {
            _context.Notifications.Add(new Notification
            {
                UserId = u.Id,
                Title = Trim(title, 120),
                Message = Trim(message, 400),
                Type = type,
                LinkUrl = linkUrl,
                CreatedAt = now
            });
            count++;
        }
        if (count > 0) await _context.SaveChangesAsync();
        return count;
    }

    public async Task NotifyAdministratorsAsync(string title, string message, NotificationType type, string? linkUrl = null)
    {
        var adminIds = await _context.UserRoles
            .Join(_context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Where(x => x.Name == Roles.Administrator)
            .Select(x => x.UserId)
            .ToListAsync();
        await NotifyUsersAsync(adminIds, title, message, type, linkUrl);
    }

    public async Task SavePreferencesAsync(string userId, bool announcements, bool rewards, bool offers, bool byEmail)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return;
        user.NoticeAnnouncements = announcements;
        user.NoticeRewards = rewards;
        user.NoticeOffers = offers;
        user.NoticeByEmail = byEmail;
        await _context.SaveChangesAsync();
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    public async Task<int> GetUnreadCountAsync(string userId)
    {
        return await _context.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);
    }

    public async Task<List<Notification>> GetForUserAsync(string userId, int take = 20)
    {
        return await _context.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .ToListAsync();
    }

    public async Task MarkAllReadAsync(string userId)
    {
        var unread = await _context.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync();
        foreach (var n in unread) n.IsRead = true;
        await _context.SaveChangesAsync();
    }

    public async Task MarkReadAsync(int notificationId, string userId)
    {
        var notification = await _context.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);
        if (notification != null)
        {
            notification.IsRead = true;
            await _context.SaveChangesAsync();
        }
    }

    /// 
    /// Best-effort email through <see cref="IEmailService"/>. It quietly does nothing until SMTP is
    /// configured (see INTEGRATIONS.md), so the app works out of the box without a mail server.
    /// 
    private async Task TrySendEmailAsync(string toEmail, string subject, string body)
    {
        if (!_email.IsConfigured) return;
        var (ok, message) = await _email.SendAsync(toEmail, subject, body);
        if (!ok) _logger.LogWarning("Notification email to {Email} was not sent: {Message}", toEmail, message);
    }
}
