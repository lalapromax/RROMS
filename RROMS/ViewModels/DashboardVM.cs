namespace RROMS.ViewModels;

/// 
/// Backs the merged Admin "Dashboard" page, which replaces the old separate
/// Dashboard + Reports pages (they showed overlapping stats from the same
/// service methods - one page with a date-range filter is simpler and avoids
/// keeping two views of the same numbers in sync).
/// 
public class DashboardVM
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    // "Right now" operational pulse - always today, regardless of the date
    // range selected below, so the kitchen/front-of-house snapshot never
    // disappears just because someone is looking at last month's report.
    public int TodayReservationsCount { get; set; }
    public int TodayOrdersCount { get; set; }
    public decimal TodayRevenue { get; set; }
    public int OrdersInKitchenCount { get; set; }
    public int PendingReservationsCount { get; set; }

    /// Percent change vs. yesterday; null when yesterday had nothing to compare against.
    public decimal? RevenueChangePercent { get; set; }
    public decimal? ReservationsChangePercent { get; set; }
    public decimal? OrdersChangePercent { get; set; }

    // Selected date-range report data.
    public SalesReportVM Sales { get; set; } = new();
    public ReservationStatsVM ReservationStats { get; set; } = new();
    public List<PopularItemVM> PopularItems { get; set; } = new();
    public List<CustomerActivityVM> CustomerActivity { get; set; } = new();
    public FeedbackSummaryVM Feedback { get; set; } = new();
    public List<RecentActivityItemVM> RecentActivity { get; set; } = new();
}
