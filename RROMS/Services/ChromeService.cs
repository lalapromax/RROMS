using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RROMS.Data;
using RROMS.Models;
using RROMS.Presentation;
using RROMS.ViewModels;

namespace RROMS.Services;

/// Everything the top bar, account menu and footer need to know about the current visitor and the restaurant.
public class ChromeInfo
{
    public RestaurantSetting? Setting { get; init; }
    public bool SignedIn { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Initials { get; init; } = string.Empty;

    /// The signed-in person's profile picture, or null when they have none (or its file is missing) and the icon is shown.
    public string? ImagePath { get; init; }
    public int Unread { get; init; }
    public int CartCount { get; init; }
    public string? TierName { get; init; }
    public int Points { get; init; }
    public int PendingReservations { get; init; }
    public int WaitingClaims { get; init; }
    public bool HasLogo { get; init; }
    public IReadOnlyList<string> HoursLines { get; init; } = Array.Empty<string>();
}

public interface IChromeService
{
    Task<ChromeInfo> GetAsync();
}

public class ChromeService : IChromeService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpContextAccessor _http;
    private readonly IWebHostEnvironment _env;
    private readonly IRewardService _rewards;
    private readonly IMemoryCache _cache;

    /// Cache key for the restaurant phone number. The error page reads it so it never has to ask a database that may be the problem.
    public const string PhoneCacheKey = "blm.phone";

    public ChromeService(ApplicationDbContext db, IHttpContextAccessor http, IWebHostEnvironment env, IRewardService rewards, IMemoryCache cache)
    {
        _cache = cache;
        _db = db;
        _http = http;
        _env = env;
        _rewards = rewards;
    }

    public async Task<ChromeInfo> GetAsync()
    {
        var ctx = _http.HttpContext;
        if (ctx != null && ctx.Items["blm.chrome"] is ChromeInfo cached)
            return cached;

        var user = ctx?.User ?? new ClaimsPrincipal();
        var signedIn = user.Identity?.IsAuthenticated == true;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);

        var setting = await _db.RestaurantSettings.AsNoTracking().FirstOrDefaultAsync();
        var hours = await _db.OperatingHours.AsNoTracking().ToListAsync();
        _cache.Set(PhoneCacheKey, setting?.Phone ?? string.Empty, TimeSpan.FromHours(6));

        string name = string.Empty, email = string.Empty;
        string? image = null;
        int unread = 0, cart = 0, points = 0, pending = 0, claims = 0;
        string? tier = null;

        if (signedIn && userId != null)
        {
            var me = await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.FullName, u.Email, u.ProfileImagePath }).FirstOrDefaultAsync();
            name = me?.FullName ?? user.Identity?.Name ?? string.Empty;
            email = me?.Email ?? string.Empty;
            // Only point the bar at a picture that is really there, so a lost upload shows the icon instead of a broken image.
            if (!string.IsNullOrWhiteSpace(me?.ProfileImagePath) && me.ProfileImagePath.StartsWith('/')
                && _env.WebRootFileProvider.GetFileInfo(me.ProfileImagePath.TrimStart('/')).Exists)
                image = me.ProfileImagePath;
            unread = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);

            if (user.IsInRole(Roles.Customer))
            {
                try
                {
                    var json = ctx!.Session.GetString("Cart");
                    if (!string.IsNullOrEmpty(json))
                        cart = JsonSerializer.Deserialize<List<CartItemVM>>(json)?.Sum(c => c.Quantity) ?? 0;
                }
                catch (Exception) { cart = 0; }

                points = await _rewards.GetPointsAsync(userId);
                tier = (await _rewards.GetTierForPointsAsync(points)).Name;
            }
            if (user.IsInRole(Roles.Staff))
                pending = await _db.Reservations.CountAsync(r => r.Status == ReservationStatus.Pending);
            if (user.IsInRole(Roles.Administrator))
                claims = await _db.RewardClaims.CountAsync(c => c.Status == RewardClaimStatus.Waiting);
        }

        var info = new ChromeInfo
        {
            Setting = setting,
            SignedIn = signedIn,
            DisplayName = name,
            Email = email,
            Initials = InitialsOf(name),
            ImagePath = image,
            Unread = unread,
            CartCount = cart,
            TierName = tier,
            Points = points,
            PendingReservations = pending,
            WaitingClaims = claims,
            HasLogo = _env.WebRootFileProvider.GetFileInfo(Brand.LogoPath.TrimStart('/')).Exists,
            HoursLines = HoursSummary(hours)
        };
        if (ctx != null) ctx.Items["blm.chrome"] = info;
        return info;
    }

    public static string InitialsOf(string? fullName)
    {
        var parts = (fullName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "";
        if (parts.Length == 1) return parts[0][..1].ToUpperInvariant();
        return (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }

    /// "Mon-Fri 11:00-22:00" style lines: consecutive days with the same hours are grouped.
    public static IReadOnlyList<string> HoursSummary(IEnumerable<OperatingHours> hours)
    {
        var order = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        var byDay = hours.ToDictionary(h => h.DayOfWeek);
        string Label(DayOfWeek d)
        {
            if (!byDay.TryGetValue(d, out var h)) return "closed";
            return h.IsClosed ? "closed" : $"{h.OpenTime:hh\\:mm}–{h.CloseTime:hh\\:mm}";
        }
        var lines = new List<string>();
        var i = 0;
        while (i < order.Length)
        {
            var j = i;
            while (j + 1 < order.Length && Label(order[j + 1]) == Label(order[i])) j++;
            var days = i == j ? Short(order[i]) : $"{Short(order[i])}–{Short(order[j])}";
            lines.Add($"{days} {Label(order[i])}");
            i = j + 1;
        }
        return lines;
    }

    private static string Short(DayOfWeek d) => d.ToString()[..3];
}
