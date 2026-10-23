using System.Text;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace RROMS.Services;

/// 
/// Turns any tabular report (a list of column headers + string rows) into CSV, PDF, or
/// Excel bytes. Kept generic and dependency-free of any specific report type so every
/// tab on Admin > Reports can reuse the same three methods.
/// 
public static class ExportHelper
{
    public static byte[] GenerateCsv(List<string> headers, List<List<string>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", row.Select(EscapeCsv)));

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string EscapeCsv(string? value)
    {
        value ??= string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    public static byte[] GeneratePdf(string title, List<string> headers, List<List<string>> rows)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text(title).FontSize(16).Bold();
                    col.Item().PaddingBottom(4).Text($"Generated {DateTime.Now:dd MMM yyyy HH:mm}")
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        foreach (var _ in headers)
                            columns.RelativeColumn();
                    });

                    table.Header(header =>
                    {
                        foreach (var h in headers)
                        {
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text(h).Bold();
                        }
                    });

                    foreach (var row in rows)
                    {
                        foreach (var cell in row)
                        {
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(cell ?? "");
                        }
                    }
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
            });
        });

        return document.GeneratePdf();
    }

    public static byte[] GenerateExcel(string sheetName, List<string> headers, List<List<string>> rows)
    {
        using var workbook = new XLWorkbook();
        var safeSheetName = sheetName.Length > 31 ? sheetName[..31] : sheetName;
        var ws = workbook.Worksheets.Add(safeSheetName);

        for (var i = 0; i < headers.Count; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(233, 225, 211);
        }

        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < rows[r].Count; c++)
            {
                ws.Cell(r + 2, c + 1).Value = rows[r][c];
            }
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
