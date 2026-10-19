using Microsoft.EntityFrameworkCore;
using RROMS.Data;

namespace RROMS.Services;

/// 
/// Sends the "new announcement" notice for announcements that were scheduled for later, the minute they go live.
/// An announcement posted for "now" is announced straight away by the page that saved it; this only covers the ones
/// waiting for their time. The flag is cleared before the notice is sent, so a crash can lose a notice but can never
/// send the same one twice. The announcement itself shows in Notices from its publish time whether or not this runs.
/// 
public class AnnouncementDispatcher : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(60);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AnnouncementDispatcher> _logger;

    public AnnouncementDispatcher(IServiceScopeFactory scopes, ILogger<AnnouncementDispatcher> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the app (and a first-run database) a moment before the first look.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); } catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(Every);
        do
        {
            try { await SendDueAsync(stoppingToken); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                // Never take the site down for this. The next minute tries again.
                _logger.LogWarning(ex, "Announcement dispatcher could not run this time.");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    private async Task SendDueAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var announcements = scope.ServiceProvider.GetRequiredService<IAnnouncementService>();

        var now = DateTime.UtcNow;
        var due = await context.Announcements
            .Include(a => a.Recipients)
            .Where(a => a.NotifyPending && a.IsActive && a.PublishAt != null && a.PublishAt <= now
                        && (a.ExpiresAt == null || a.ExpiresAt >= now))
            .ToListAsync(ct);
        if (due.Count == 0) return;

        foreach (var a in due) a.NotifyPending = false;
        await context.SaveChangesAsync(ct);

        foreach (var a in due)
        {
            var sent = await announcements.NotifyAudienceAsync(a);
            _logger.LogInformation("Announcement {Id} went live; {Count} guests were told.", a.Id, sent);
        }
    }
}
