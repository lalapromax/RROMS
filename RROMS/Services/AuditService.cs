using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;

namespace RROMS.Services;

public interface IAuditService
{
    Task LogAsync(string adminUserId, string adminName, string action, string? details = null);
    Task<List<AdminAuditLog>> GetRecentAsync(int take = 50);
}

public class AuditService : IAuditService
{
    private readonly ApplicationDbContext _context;

    public AuditService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task LogAsync(string adminUserId, string adminName, string action, string? details = null)
    {
        _context.AdminAuditLogs.Add(new AdminAuditLog
        {
            AdminUserId = adminUserId,
            AdminNameSnapshot = adminName,
            Action = action,
            Details = details,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }

    public async Task<List<AdminAuditLog>> GetRecentAsync(int take = 50) =>
        await _context.AdminAuditLogs.OrderByDescending(a => a.CreatedAt).Take(take).ToListAsync();
}
