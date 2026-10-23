namespace RROMS.ViewModels;

public class SalesReportVM
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int TotalOrders { get; set; }
    public decimal TotalSubTotal { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal AverageOrderValue { get; set; }
    public List<DailySalesPointVM> DailyBreakdown { get; set; } = new();
}

public class DailySalesPointVM
{
    public DateTime Date { get; set; }
    public int OrderCount { get; set; }
    public decimal Revenue { get; set; }
}

public class PopularItemVM
{
    public int MenuItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int QuantitySold { get; set; }
    public decimal RevenueGenerated { get; set; }
}

public class ReservationStatsVM
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int TotalReservations { get; set; }
    public int Pending { get; set; }
    public int Confirmed { get; set; }
    public int Completed { get; set; }
    public int Cancelled { get; set; }
    public int NoShow { get; set; }
    public double AveragePartySize { get; set; }
    public List<HourCountVM> ByHour { get; set; } = new();
    public List<TableUtilizationVM> ByTable { get; set; } = new();
}

/// Aggregated customer feedback for the admin dashboard - section 6.H of the
/// brief asks for this to feed real operational decisions, not just raw comments.
public class FeedbackSummaryVM
{
    /// Fewer answers than this and an average is just noise, so the dashboard says "not enough data".
    public const int MinResponsesForAverage = 3;
    /// Net-promoter style scores swing wildly on tiny samples, so they need more answers.
    public const int MinResponsesForNps = 5;

    public int TotalCount { get; set; }
    public double AverageRating { get; set; }

    /// Star rating (1-5) -&gt; how many reviews gave it, always containing all 5 keys.
    public Dictionary<int, int> RatingCounts { get; set; } = new() { [1] = 0, [2] = 0, [3] = 0, [4] = 0, [5] = 0 };

    /// Food / service / cleanliness / wait / value, each averaged over only the reviews that answered it.
    public List<FeedbackMetricVM> Metrics { get; set; } = new();

    public NpsVM Nps { get; set; } = new();

    /// Most frequently flagged problems (from the fixed issue-tag list), highest first.
    public List<IssueCountVM> TopIssues { get; set; } = new();

    /// Average overall rating per week (or per month for long ranges).
    public List<FeedbackTrendPointVM> Trend { get; set; } = new();
    public string TrendPeriod { get; set; } = "week";

    /// A trend line needs reviews in at least two different periods.
    public bool TrendHasEnoughData => Trend.Count >= 2;
}

public class FeedbackMetricVM
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public double Average { get; set; }
    public int Responses { get; set; }
    public bool HasEnoughData => Responses >= FeedbackSummaryVM.MinResponsesForAverage;
}

public class NpsVM
{
    public int Responses { get; set; }
    public int Promoters { get; set; }
    public int Passives { get; set; }
    public int Detractors { get; set; }

    /// -100..100; null until there are enough answers to mean anything.
    public int? Score { get; set; }
    public bool HasEnoughData => Responses >= FeedbackSummaryVM.MinResponsesForNps;
}

public class IssueCountVM
{
    public string Tag { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class FeedbackTrendPointVM
{
    public DateTime PeriodStart { get; set; }
    public string Label { get; set; } = string.Empty;
    public double AverageRating { get; set; }
    public int Count { get; set; }
}

/// One review as shown to an administrator on the dashboard (authorised review data).
public class RecentReviewVM
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int Rating { get; set; }
    public string Context { get; set; } = string.Empty;
    public string? Comments { get; set; }
    public List<string> IssueTags { get; set; } = new();
    public List<string> Images { get; set; } = new();
    public List<string> ScoreSummary { get; set; } = new();
}

/// One line in the dashboard's "recent activity" feed - a reservation or an
/// order, whichever happened most recently, so staff see one combined timeline
/// instead of two separate lists.
public class RecentActivityItemVM
{
    public string Kind { get; set; } = string.Empty; // "Reservation" or "Order"
    /// "Reservation, T3" or "Order, T6": what happened and where.
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTime When { get; set; }
    public string? Url { get; set; }
}

public class HourCountVM
{
    public int Hour { get; set; }
    public int Count { get; set; }
}

public class TableUtilizationVM
{
    public string TableNumber { get; set; } = string.Empty;
    public int BookingCount { get; set; }
}

public class CustomerActivityVM
{
    public string CustomerId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int ReservationCount { get; set; }
    public int OrderCount { get; set; }
    public decimal TotalSpent { get; set; }
    public DateTime? LastVisit { get; set; }
}
