using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;

namespace RROMS.Services;

public interface INotificationService
{
    Task NotifyAsync(string userId, string title, string message, NotificationType type, string? linkUrl = null, bool sendEmail = true);
    Task NotifyAllCustomersAsync(string title, string message, NotificationType type, string? linkUrl = null);
    Task<int> GetUnreadCountAsync(string userId);
    Task<List<Notification>> GetForUserAsync(string userId, int take = 20);
    Task MarkAllReadAsync(string userId);
    Task MarkReadAsync(int notificationId, string userId);
}

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _config;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ApplicationDbContext context, IConfiguration config, ILogger<NotificationService> logger)
    {
        _context = context;
        _config = config;
        _logger = logger;
    }

    public async Task NotifyAsync(string userId, string title, string message, NotificationType type, string? linkUrl = null, bool sendEmail = true)
    {
        var notification = new Notification
        {
            UserId = userId,
            Title = title,
            Message = message,
            Type = type,
            LinkUrl = linkUrl,
            CreatedAt = DateTime.UtcNow
        };
        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        if (sendEmail)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user?.Email != null)
                await TrySendEmailAsync(user.Email, title, message);
        }
    }

    public async Task NotifyAllCustomersAsync(string title, string message, NotificationType type, string? linkUrl = null)
    {
        var customerIds = await _context.UserRoles
            .Join(_context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Where(x => x.Name == Roles.Customer)
            .Select(x => x.UserId)
            .ToListAsync();

        foreach (var id in customerIds)
        {
            _context.Notifications.Add(new Notification
            {
                UserId = id,
                Title = title,
                Message = message,
                Type = type,
                LinkUrl = linkUrl,
                CreatedAt = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync();
    }

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

    /// <summary>
    /// Best-effort SMTP email. Silently no-ops when SmtpSettings:Enabled is false (the default),
    /// so the app works out of the box without any mail server configured. Set Enabled to true
    /// and fill in Host/Username/Password in appsettings.json (or user-secrets) to turn it on.
    /// </summary>
    private async Task TrySendEmailAsync(string toEmail, string subject, string body)
    {
        var enabled = _config.GetValue<bool>("SmtpSettings:Enabled");
        if (!enabled) return;

        try
        {
            var host = _config["SmtpSettings:Host"];
            var port = _config.GetValue<int>("SmtpSettings:Port");
            var useSsl = _config.GetValue<bool>("SmtpSettings:UseSsl");
            var username = _config["SmtpSettings:Username"];
            var password = _config["SmtpSettings:Password"];
            var fromName = _config["SmtpSettings:FromName"] ?? "RROMS Restaurant";
            var fromAddress = _config["SmtpSettings:FromAddress"] ?? username ?? "no-reply@rroms.local";

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = useSsl,
                Credentials = new NetworkCredential(username, password)
            };

            using var mail = new MailMessage
            {
                From = new MailAddress(fromAddress, fromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = false
            };
            mail.To.Add(toEmail);

            await client.SendMailAsync(mail);
        }
        catch (Exception ex)
        {
            // Email is a courtesy channel - never let a mail failure break a reservation/order flow.
            _logger.LogWarning(ex, "Failed to send notification email to {Email}", toEmail);
        }
    }
}
