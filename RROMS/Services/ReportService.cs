using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Presentation;
using RROMS.ViewModels;

namespace RROMS.Services;

public interface IReportService
{
    Task<SalesReportVM> GetSalesReportAsync(DateTime start, DateTime end);
    Task<List<PopularItemVM>> GetPopularItemsAsync(DateTime start, DateTime end, int top = 10);
    Task<ReservationStatsVM> GetReservationStatsAsync(DateTime start, DateTime end);
    Task<List<CustomerActivityVM>> GetCustomerActivityAsync(DateTime start, DateTime end);
    Task<FeedbackSummaryVM> GetFeedbackSummaryAsync(DateTime start, DateTime end);
    Task<List<RecentReviewVM>> GetRecentReviewsAsync(DateTime start, DateTime end, int? maxStars, int take = 8);
    Task<List<RecentActivityItemVM>> GetRecentActivityAsync(int take = 8);
    Task<DashboardVM> GetDashboardDataAsync(DateTime start, DateTime end);
}

public class ReportService : IReportService
{
    private readonly ApplicationDbContext _context;

    /// Orders, feedback and activity are stored in UTC, while the restaurant thinks in local days. This turns the start of a local day into the UTC instant to compare stored times against.
    private static DateTime Utc(DateTime localDay) => DateTime.SpecifyKind(localDay.Date, DateTimeKind.Local).ToUniversalTime();

    public ReportService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<SalesReportVM> GetSalesReportAsync(DateTime start, DateTime end)
    {
        var fromUtc = Utc(start);
        var toUtc = Utc(end.Date.AddDays(1));
        var orders = await _context.Orders
            .Where(o => o.OrderDate >= fromUtc && o.OrderDate < toUtc && o.Status != OrderStatus.Cancelled)
            .ToListAsync();

        var vm = new SalesReportVM
        {
            StartDate = start.Date,
            EndDate = end.Date,
            TotalOrders = orders.Count,
            TotalSubTotal = orders.Sum(o => o.SubTotal),
            TotalDiscount = orders.Sum(o => o.DiscountAmount),
            TotalTax = orders.Sum(o => o.TaxAmount),
            TotalRevenue = orders.Sum(o => o.TotalAmount),
            AverageOrderValue = orders.Count > 0 ? Math.Round(orders.Average(o => o.TotalAmount), 2) : 0m
        };

        vm.DailyBreakdown = orders
            .GroupBy(o => o.OrderDate.ToLocalTime().Date)
            .OrderBy(g => g.Key)
            .Select(g => new DailySalesPointVM { Date = g.Key, OrderCount = g.Count(), Revenue = g.Sum(o => o.TotalAmount) })
            .ToList();

        return vm;
    }

    public async Task<List<PopularItemVM>> GetPopularItemsAsync(DateTime start, DateTime end, int top = 10)
    {
        var fromUtc = Utc(start);
        var toUtc = Utc(end.Date.AddDays(1));

        var query = from oi in _context.OrderItems
                     join o in _context.Orders on oi.OrderId equals o.Id
                     join mi in _context.MenuItems on oi.MenuItemId equals mi.Id
                     join mc in _context.MenuCategories on mi.MenuCategoryId equals mc.Id
                     where o.OrderDate >= fromUtc && o.OrderDate < toUtc && o.Status != OrderStatus.Cancelled
                     group oi by new { oi.MenuItemId, ItemName = mi.Name, CategoryName = mc.Name } into g
                     select new PopularItemVM
                     {
                         MenuItemId = g.Key.MenuItemId,
                         Name = g.Key.ItemName,
                         Category = g.Key.CategoryName,
                         QuantitySold = g.Sum(x => x.Quantity),
                         RevenueGenerated = g.Sum(x => x.Subtotal)
                     };

        return await query
            .OrderByDescending(p => p.QuantitySold)
            .Take(top)
            .ToListAsync();
    }

    public async Task<ReservationStatsVM> GetReservationStatsAsync(DateTime start, DateTime end)
    {
        var endExclusive = end.Date.AddDays(1);
        var reservations = await _context.Reservations
            .Include(r => r.Table)
            .Where(r => r.ReservationDateTime >= start.Date && r.ReservationDateTime < endExclusive)
            .ToListAsync();

        var vm = new ReservationStatsVM
        {
            StartDate = start.Date,
            EndDate = end.Date,
            TotalReservations = reservations.Count,
            Pending = reservations.Count(r => r.Status == ReservationStatus.Pending),
            Confirmed = reservations.Count(r => r.Status == ReservationStatus.Confirmed),
            Completed = reservations.Count(r => r.Status == ReservationStatus.Completed),
            Cancelled = reservations.Count(r => r.Status == ReservationStatus.Cancelled),
            NoShow = reservations.Count(r => r.Status == ReservationStatus.NoShow),
            AveragePartySize = reservations.Count > 0 ? Math.Round(reservations.Average(r => r.PartySize), 1) : 0
        };

        vm.ByHour = reservations
            .GroupBy(r => r.ReservationDateTime.Hour)
            .OrderBy(g => g.Key)
            .Select(g => new HourCountVM { Hour = g.Key, Count = g.Count() })
            .ToList();

        vm.ByTable = reservations
            .Where(r => r.Table != null)
            .GroupBy(r => r.Table!.TableNumber)
            .OrderByDescending(g => g.Count())
            .Select(g => new TableUtilizationVM { TableNumber = g.Key, BookingCount = g.Count() })
            .ToList();

        return vm;
    }

    public async Task<List<CustomerActivityVM>> GetCustomerActivityAsync(DateTime start, DateTime end)
    {
        var endExclusive = end.Date.AddDays(1);

        var customers = await _context.Users
            .Join(_context.UserRoles, u => u.Id, ur => ur.UserId, (u, ur) => new { u, ur.RoleId })
            .Join(_context.Roles, x => x.RoleId, r => r.Id, (x, r) => new { x.u, r.Name })
            .Where(x => x.Name == Roles.Customer)
            .Select(x => x.u)
            .ToListAsync();

        var reservations = await _context.Reservations
            .Where(r => r.ReservationDateTime >= start.Date && r.ReservationDateTime < endExclusive)
            .ToListAsync();

        var fromUtc = Utc(start);
        var toUtc = Utc(end.Date.AddDays(1));
        var orders = await _context.Orders
            .Where(o => o.OrderDate >= fromUtc && o.OrderDate < toUtc && o.Status != OrderStatus.Cancelled)
            .ToListAsync();

        var result = new List<CustomerActivityVM>();
        foreach (var c in customers)
        {
            var customerReservations = reservations.Where(r => r.CustomerId == c.Id).ToList();
            var customerOrders = orders.Where(o => o.CustomerId == c.Id).ToList();
            if (customerReservations.Count == 0 && customerOrders.Count == 0) continue;

            var lastVisit = new[] { customerReservations.Select(r => (DateTime?)r.ReservationDateTime).DefaultIfEmpty(null).Max(),
                                     customerOrders.Select(o => (DateTime?)o.OrderDate.ToLocalTime()).DefaultIfEmpty(null).Max() }
                                     .Where(d => d.HasValue).DefaultIfEmpty(null).Max();

            result.Add(new CustomerActivityVM
            {
                CustomerId = c.Id,
                FullName = c.FullName,
                Email = c.Email ?? "",
                ReservationCount = customerReservations.Count,
                OrderCount = customerOrders.Count,
                TotalSpent = customerOrders.Sum(o => o.TotalAmount),
                LastVisit = lastVisit
            });
        }

        return result.OrderByDescending(r => r.TotalSpent).ToList();
    }

    public async Task<FeedbackSummaryVM> GetFeedbackSummaryAsync(DateTime start, DateTime end)
    {
        var fromUtc = Utc(start);
        var toUtc = Utc(end.Date.AddDays(1));
        var rows = await _context.Feedbacks
            .Where(f => f.CreatedAt >= fromUtc && f.CreatedAt < toUtc)
            .Select(f => new
            {
                f.CreatedAt,
                f.Rating,
                f.FoodRating,
                f.ServiceRating,
                f.CleanlinessRating,
                f.WaitTimeRating,
                f.ValueRating,
                f.RecommendScore,
                f.IssueTags
            })
            .ToListAsync();

        var ratings = rows.Select(r => r.Rating).ToList();
        var vm = new FeedbackSummaryVM
        {
            TotalCount = ratings.Count,
            AverageRating = ratings.Count > 0 ? Math.Round(ratings.Average(), 2) : 0
        };
        foreach (var r in ratings)
        {
            if (vm.RatingCounts.ContainsKey(r)) vm.RatingCounts[r]++;
        }

        // Each metric is averaged over only the reviews that answered it (null = skipped / older review).
        vm.Metrics = new List<FeedbackMetricVM>
        {
            BuildMetric("food", rows.Select(r => r.FoodRating)),
            BuildMetric("service", rows.Select(r => r.ServiceRating)),
            BuildMetric("cleanliness", rows.Select(r => r.CleanlinessRating)),
            BuildMetric("wait", rows.Select(r => r.WaitTimeRating)),
            BuildMetric("value", rows.Select(r => r.ValueRating))
        };

        // Net-promoter style: 9-10 promoters, 7-8 passives, 0-6 detractors.
        var recommend = rows.Where(r => r.RecommendScore.HasValue).Select(r => r.RecommendScore!.Value).ToList();
        vm.Nps.Responses = recommend.Count;
        vm.Nps.Promoters = recommend.Count(s => s >= 9);
        vm.Nps.Passives = recommend.Count(s => s >= 7 && s <= 8);
        vm.Nps.Detractors = recommend.Count(s => s <= 6);
        if (vm.Nps.HasEnoughData)
            vm.Nps.Score = (int)Math.Round((vm.Nps.Promoters - vm.Nps.Detractors) * 100.0 / vm.Nps.Responses);

        vm.TopIssues = rows
            .SelectMany(r => (r.IssueTags ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .GroupBy(tag => tag)
            .Select(g => new IssueCountVM { Tag = g.Key, Count = g.Count() })
            .OrderByDescending(i => i.Count)
            .ThenBy(i => i.Tag)
            .Take(5)
            .ToList();

        // Trend: weekly buckets (monthly for ranges over ~4 months), only periods that have reviews.
        var byMonth = (end.Date - start.Date).TotalDays > 120;
        vm.TrendPeriod = byMonth ? "month" : "week";
        vm.Trend = rows
            .GroupBy(r => byMonth
                ? new DateTime(r.CreatedAt.ToLocalTime().Year, r.CreatedAt.ToLocalTime().Month, 1)
                : r.CreatedAt.ToLocalTime().Date.AddDays(-(((int)r.CreatedAt.ToLocalTime().DayOfWeek + 6) % 7)))
            .OrderBy(g => g.Key)
            .Select(g => new FeedbackTrendPointVM
            {
                PeriodStart = g.Key,
                Label = byMonth ? g.Key.ToString("MMM yyyy") : g.Key.ToString("d MMM"),
                AverageRating = Math.Round(g.Average(r => r.Rating), 2),
                Count = g.Count()
            })
            .ToList();

        return vm;
    }

    private static FeedbackMetricVM BuildMetric(string key, IEnumerable<int?> values)
    {
        var answered = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        var label = ReviewOptions.Metrics.First(m => m.Key == key).Label;
        return new FeedbackMetricVM
        {
            Key = key,
            Label = label,
            Responses = answered.Count,
            Average = answered.Count > 0 ? Math.Round(answered.Average(), 2) : 0
        };
    }

    public async Task<List<RecentReviewVM>> GetRecentReviewsAsync(DateTime start, DateTime end, int? maxStars, int take = 8)
    {
        var fromUtc = Utc(start);
        var toUtc = Utc(end.Date.AddDays(1));
        var query = _context.Feedbacks
            .Include(f => f.Customer)
            .Where(f => f.CreatedAt >= fromUtc && f.CreatedAt < toUtc);
        if (maxStars.HasValue)
            query = query.Where(f => f.Rating <= maxStars.Value);

        var reviews = await query.OrderByDescending(f => f.CreatedAt).Take(take).ToListAsync();

        return reviews.Select(f =>
        {
            var scores = new List<string>();
            if (f.FoodRating.HasValue) scores.Add($"Food {f.FoodRating}/5");
            if (f.ServiceRating.HasValue) scores.Add($"Service {f.ServiceRating}/5");
            if (f.CleanlinessRating.HasValue) scores.Add($"Cleanliness {f.CleanlinessRating}/5");
            if (f.WaitTimeRating.HasValue) scores.Add($"Wait {f.WaitTimeRating}/5");
            if (f.ValueRating.HasValue) scores.Add($"Value {f.ValueRating}/5");
            if (f.RecommendScore.HasValue) scores.Add($"Recommend {f.RecommendScore}/10");

            return new RecentReviewVM
            {
                Id = f.Id,
                CustomerName = f.ShowFirstName ? f.Customer?.FullName ?? "A guest" : "A guest",
                CreatedAt = f.CreatedAt.ToLocalTime(),
                Rating = f.Rating,
                Context = f.OrderId.HasValue ? $"Order #{f.OrderId}" : f.ReservationId.HasValue ? "Dining visit" : "General",
                Comments = f.Comments,
                IssueTags = f.IssueTagList().ToList(),
                Images = f.ImageList().ToList(),
                ScoreSummary = scores
            };
        }).ToList();
    }

    public async Task<List<RecentActivityItemVM>> GetRecentActivityAsync(int take = 8)
    {
        // Pull the raw rows first, then format in memory - an enum-to-string
        // or currency-format interpolation inside a LINQ-to-Entities .Select()
        // can't translate to SQL, so building the display text here (not in
        // the query) keeps this safe regardless of the EF provider in use.
        var recentReservations = await _context.Reservations
            .Include(r => r.Customer).Include(r => r.Table)
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .ToListAsync();

        var recentOrders = await _context.Orders
            .Include(o => o.Customer).Include(o => o.Table)
            .OrderByDescending(o => o.OrderDate)
            .Take(take)
            .ToListAsync();

        var items = recentReservations
            .Select(r => new RecentActivityItemVM
            {
                Kind = "Reservation",
                Title = r.Table != null ? $"Reservation, {r.Table.TableNumber}" : "Reservation",
                Summary = $"{(string.IsNullOrWhiteSpace(r.ContactName) ? r.Customer?.FullName ?? "A guest" : r.ContactName)}, {r.PartySize} {(r.PartySize == 1 ? "guest" : "guests")} at {r.ReservationDateTime:HH:mm}",
                When = r.CreatedAt,
                Url = "/Staff/Reservations"
            })
            .Concat(recentOrders.Select(o => new RecentActivityItemVM
            {
                Kind = "Order",
                Title = o.Table != null ? $"Order, {o.Table.TableNumber}" : "Order",
                Summary = $"{o.Customer?.FullName ?? "A guest"}, {Ui.Rm(o.TotalAmount)} ({o.Status})",
                When = o.OrderDate,
                Url = "/Staff/Orders"
            }));

        return items
            .OrderByDescending(a => a.When)
            .Take(take)
            .ToList();
    }

    public async Task<DashboardVM> GetDashboardDataAsync(DateTime start, DateTime end)
    {
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);
        var yesterday = today.AddDays(-1);

        var todayUtc = Utc(today);
        var tomorrowUtc = Utc(tomorrow);
        var yesterdayUtc = Utc(yesterday);
        var todayOrders = await _context.Orders.Where(o => o.OrderDate >= todayUtc && o.OrderDate < tomorrowUtc).ToListAsync();
        var yesterdayOrders = await _context.Orders.Where(o => o.OrderDate >= yesterdayUtc && o.OrderDate < todayUtc).ToListAsync();
        var todayReservationsCount = await _context.Reservations.CountAsync(r => r.ReservationDateTime >= today && r.ReservationDateTime < tomorrow);
        var yesterdayReservationsCount = await _context.Reservations.CountAsync(r => r.ReservationDateTime >= yesterday && r.ReservationDateTime < today);
        var pendingReservations = await _context.Reservations.CountAsync(r => r.Status == ReservationStatus.Pending);
        var kitchenOrders = await _context.Orders.CountAsync(o => o.Status == OrderStatus.Placed || o.Status == OrderStatus.Preparing);

        var todayRevenue = todayOrders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.TotalAmount);
        var yesterdayRevenue = yesterdayOrders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.TotalAmount);

        var sales = await GetSalesReportAsync(start, end);
        var reservationStats = await GetReservationStatsAsync(start, end);
        var popular = await GetPopularItemsAsync(start, end, 10);
        var customerActivity = await GetCustomerActivityAsync(start, end);
        var feedback = await GetFeedbackSummaryAsync(start, end);
        var recentActivity = await GetRecentActivityAsync(8);

        return new DashboardVM
        {
            StartDate = start.Date,
            EndDate = end.Date,

            TodayReservationsCount = todayReservationsCount,
            TodayOrdersCount = todayOrders.Count,
            TodayRevenue = todayRevenue,
            PendingReservationsCount = pendingReservations,
            OrdersInKitchenCount = kitchenOrders,
            RevenueChangePercent = PercentChange(yesterdayRevenue, todayRevenue),
            ReservationsChangePercent = PercentChange(yesterdayReservationsCount, todayReservationsCount),
            OrdersChangePercent = PercentChange(yesterdayOrders.Count, todayOrders.Count),

            Sales = sales,
            ReservationStats = reservationStats,
            PopularItems = popular,
            CustomerActivity = customerActivity,
            Feedback = feedback,
            RecentActivity = recentActivity
        };
    }

    /// Percent change from baseline to current; null (rather than a misleading
    /// "infinite"/0% swing) when there is no baseline to compare against.
    private static decimal? PercentChange(decimal baseline, decimal current)
    {
        if (baseline == 0) return current == 0 ? 0 : null;
        return Math.Round((current - baseline) / baseline * 100m, 1);
    }

    private static decimal? PercentChange(int baseline, int current) => PercentChange((decimal)baseline, current);
}
