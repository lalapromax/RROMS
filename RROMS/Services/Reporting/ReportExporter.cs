using RROMS.ViewModels;

namespace RROMS.Services.Reporting;

/// <summary>
/// The one entry point the Overview page uses for its CSV, PDF and Excel downloads: the same report data in three shapes.
/// </summary>
public static class ReportExporter
{
    public sealed record ExportFile(byte[] Bytes, string ContentType, string Extension);

    /// <param name="format">"pdf", "excel" or anything else for CSV.</param>
    public static ExportFile Export(string? format, DashboardVM overview, List<PopularItemVM> popular, List<CustomerActivityVM> customers,
        List<RecentReviewVM> reviews, DateTime generatedAt)
    {
        var data = ReportData.Build(overview, popular, customers, reviews, generatedAt);
        return format switch
        {
            "pdf" => new ExportFile(ReportPdf.Build(data), "application/pdf", "pdf"),
            "excel" => new ExportFile(ReportXlsx.Build(data), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx"),
            _ => new ExportFile(ReportCsv.Build(data), "text/csv; charset=utf-8", "csv")
        };
    }
}
