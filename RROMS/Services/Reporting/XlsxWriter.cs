using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace RROMS.Services.Reporting;

/// <summary>How a cell looks. Records compare by value, so two cells with the same look share one style in the file.</summary>
internal sealed record XlsxStyle(
    string Font = "Calibri", double Size = 10, bool Bold = false, bool Italic = false, string Color = "442D1C",
    string? Fill = null, string Halign = "", string Valign = "center", bool Wrap = false, string? NumFmt = null,
    string Border = "", string BorderColor = "E8D1A7", int Indent = 0);

/// <summary>A formula together with the answer to show before Excel recalculates.</summary>
internal sealed record XlsxFormula(string Formula, double Cached);

internal enum ChartKind { Column, Bar, Line, Area, Doughnut }

/// <summary>One data series of a chart, read from columns of a sheet (categories in one column, values in another).</summary>
internal sealed class XlsxSeries
{
    public string Name { get; set; } = "";
    public XlsxSheet Sheet { get; set; } = null!;
    public int CategoryColumn { get; set; }
    public int ValueColumn { get; set; }
    public int FirstRow { get; set; }
    public int LastRow { get; set; }
    public string Color { get; set; } = "743014";
    public string[]? PointColors { get; set; }
}

internal sealed class XlsxChart
{
    public ChartKind Kind { get; set; }
    public string Title { get; set; } = "";
    public List<XlsxSeries> Series { get; } = new();
    /// <summary>Where the chart sits, in cells (1-based; the chart fills from the top-left of FromRow/FromColumn to the top-left of ToRow/ToColumn).</summary>
    public int FromRow { get; set; }
    public int FromColumn { get; set; }
    public int ToRow { get; set; }
    public int ToColumn { get; set; }
    public bool Legend { get; set; }
    public bool DataLabels { get; set; }
    public string AxisFormat { get; set; } = "#,##0";
    public double? AxisMin { get; set; }
    public double? AxisMax { get; set; }
}

internal sealed class XlsxSheet
{
    private sealed class Cell { public object? Value; public XlsxStyle? Style; }

    private readonly SortedDictionary<int, SortedDictionary<int, Cell>> _rows = new();
    private readonly SortedDictionary<int, double> _widths = new();
    private readonly Dictionary<int, double> _heights = new();
    private readonly List<(int R1, int C1, int R2, int C2)> _merges = new();
    private readonly List<(int R1, int C1, int R2, int C2, string Color)> _dataBars = new();
    private (int R1, int C1, int R2, int C2)? _filter;

    internal string? FilterRange => _filter == null ? null : AbsRange(_filter.Value.R1, _filter.Value.C1, _filter.Value.R2, _filter.Value.C2);

    public string Name { get; }
    public int FreezeRows { get; set; }
    public bool Gridlines { get; set; }
    public string? TabColor { get; set; }
    public List<XlsxChart> Charts { get; } = new();

    internal XlsxSheet(string name) { Name = name; }

    public void Set(int row, int col, object? value, XlsxStyle? style = null)
    {
        if (!_rows.TryGetValue(row, out var r)) _rows[row] = r = new SortedDictionary<int, Cell>();
        r[col] = new Cell { Value = value, Style = style };
    }

    public object? Get(int row, int col) => _rows.TryGetValue(row, out var r) && r.TryGetValue(col, out var c) ? c.Value : null;

    public void Width(int col, double width) => _widths[col] = width;
    public void WidthAtLeast(int col, double width) { if (!_widths.TryGetValue(col, out var w) || w < width) _widths[col] = width; }
    public void Height(int row, double height) => _heights[row] = height;
    public void Merge(int r1, int c1, int r2, int c2) => _merges.Add((r1, c1, r2, c2));
    public void AutoFilter(int r1, int c1, int r2, int c2) => _filter = (r1, c1, r2, c2);
    public void DataBar(int r1, int c1, int r2, int c2, string color) => _dataBars.Add((r1, c1, r2, c2, color));

    public static string ColumnName(int col)
    {
        var s = "";
        while (col > 0) { col--; s = (char)('A' + col % 26) + s; col /= 26; }
        return s;
    }

    public static string CellRef(int row, int col) => ColumnName(col) + row.ToString(CultureInfo.InvariantCulture);
    internal string QuotedName => "'" + Name.Replace("'", "''") + "'";
    internal string AbsRange(int r1, int c1, int r2, int c2) =>
        QuotedName + "!$" + ColumnName(c1) + "$" + r1 + (r1 == r2 && c1 == c2 ? "" : ":$" + ColumnName(c2) + "$" + r2);

    internal string BuildXml(XlsxBook book, bool selected, bool hasDrawing)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        sb.Append("<sheetPr>");
        if (TabColor != null) sb.Append("<tabColor rgb=\"FF").Append(TabColor).Append("\"/>");
        sb.Append("<pageSetUpPr fitToPage=\"1\"/></sheetPr>");

        var maxRow = _rows.Count == 0 ? 1 : _rows.Keys.Max();
        var maxCol = _rows.Count == 0 ? 1 : _rows.Values.Where(r => r.Count > 0).Select(r => r.Keys.Max()).DefaultIfEmpty(1).Max();
        sb.Append("<dimension ref=\"A1:").Append(CellRef(maxRow, maxCol)).Append("\"/>");

        sb.Append("<sheetViews><sheetView");
        if (!Gridlines) sb.Append(" showGridLines=\"0\"");
        if (selected) sb.Append(" tabSelected=\"1\"");
        sb.Append(" workbookViewId=\"0\">");
        if (FreezeRows > 0)
        {
            sb.Append("<pane ySplit=\"").Append(FreezeRows).Append("\" topLeftCell=\"A").Append(FreezeRows + 1).Append("\" activePane=\"bottomLeft\" state=\"frozen\"/>");
            sb.Append("<selection pane=\"bottomLeft\" activeCell=\"A").Append(FreezeRows + 1).Append("\" sqref=\"A").Append(FreezeRows + 1).Append("\"/>");
        }
        sb.Append("</sheetView></sheetViews>");
        sb.Append("<sheetFormatPr defaultRowHeight=\"15\"/>");

        if (_widths.Count > 0)
        {
            sb.Append("<cols>");
            foreach (var (col, w) in _widths)
                sb.Append("<col min=\"").Append(col).Append("\" max=\"").Append(col).Append("\" width=\"").Append(w.ToString("0.##", CultureInfo.InvariantCulture)).Append("\" customWidth=\"1\"/>");
            sb.Append("</cols>");
        }

        sb.Append("<sheetData>");
        var rowNumbers = _rows.Keys.Union(_heights.Keys).OrderBy(x => x);
        foreach (var rn in rowNumbers)
        {
            sb.Append("<row r=\"").Append(rn).Append('"');
            if (_heights.TryGetValue(rn, out var ht)) sb.Append(" ht=\"").Append(ht.ToString("0.##", CultureInfo.InvariantCulture)).Append("\" customHeight=\"1\"");
            sb.Append('>');
            if (_rows.TryGetValue(rn, out var cells))
            {
                foreach (var (cn, cell) in cells)
                {
                    var s = book.StyleId(cell.Style);
                    var sAttr = s == 0 ? "" : " s=\"" + s + "\"";
                    var r = CellRef(rn, cn);
                    switch (cell.Value)
                    {
                        case null:
                            sb.Append("<c r=\"").Append(r).Append('"').Append(sAttr).Append("/>");
                            break;
                        case string text:
                            if (text.Length == 0) { sb.Append("<c r=\"").Append(r).Append('"').Append(sAttr).Append("/>"); break; }
                            sb.Append("<c r=\"").Append(r).Append('"').Append(sAttr).Append(" t=\"s\"><v>").Append(book.StringId(text)).Append("</v></c>");
                            break;
                        case bool b:
                            sb.Append("<c r=\"").Append(r).Append('"').Append(sAttr).Append(" t=\"b\"><v>").Append(b ? 1 : 0).Append("</v></c>");
                            break;
                        case DateTime dt:
                            sb.Append("<c r=\"").Append(r).Append('"').Append(sAttr).Append("><v>").Append(dt.ToOADate().ToString("R", CultureInfo.InvariantCulture)).Append("</v></c>");
                            break;
                        case XlsxFormula f:
                            sb.Append("<c r=\"").Append(r).Append('"').Append(sAttr).Append("><f>").Append(XlsxBook.Esc(f.Formula)).Append("</f><v>")
                              .Append(f.Cached.ToString("R", CultureInfo.InvariantCulture)).Append("</v></c>");
                            break;
                        default:
                            sb.Append("<c r=\"").Append(r).Append('"').Append(sAttr).Append("><v>").Append(XlsxBook.Number(cell.Value)).Append("</v></c>");
                            break;
                    }
                }
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData>");

        if (_filter != null)
            sb.Append("<autoFilter ref=\"").Append(CellRef(_filter.Value.R1, _filter.Value.C1)).Append(':').Append(CellRef(_filter.Value.R2, _filter.Value.C2)).Append("\"/>");
        if (_merges.Count > 0)
        {
            sb.Append("<mergeCells count=\"").Append(_merges.Count).Append("\">");
            foreach (var m in _merges) sb.Append("<mergeCell ref=\"").Append(CellRef(m.R1, m.C1)).Append(':').Append(CellRef(m.R2, m.C2)).Append("\"/>");
            sb.Append("</mergeCells>");
        }
        var priority = 1;
        foreach (var bar in _dataBars)
        {
            sb.Append("<conditionalFormatting sqref=\"").Append(CellRef(bar.R1, bar.C1)).Append(':').Append(CellRef(bar.R2, bar.C2)).Append("\">")
              .Append("<cfRule type=\"dataBar\" priority=\"").Append(priority++).Append("\"><dataBar><cfvo type=\"num\" val=\"0\"/><cfvo type=\"max\"/><color rgb=\"FF")
              .Append(bar.Color).Append("\"/></dataBar></cfRule></conditionalFormatting>");
        }
        sb.Append("<pageMargins left=\"0.5\" right=\"0.5\" top=\"0.6\" bottom=\"0.6\" header=\"0.3\" footer=\"0.3\"/>");
        sb.Append("<pageSetup paperSize=\"9\" orientation=\"landscape\" fitToHeight=\"0\"/>");
        if (hasDrawing) sb.Append("<drawing r:id=\"rId1\"/>");
        sb.Append("</worksheet>");
        return sb.ToString();
    }
}

/// <summary>
/// Writes a finished .xlsx file: sheets of text, numbers and dates with colours, column widths, frozen headers, filters,
/// in-cell data bars and real Excel charts. Written by hand on top of the .NET zip support so that it needs no
/// spreadsheet library and the charts are guaranteed to be there.
/// </summary>
internal sealed class XlsxBook
{
    private readonly List<XlsxSheet> _sheets = new();
    private readonly List<string> _strings = new();
    private readonly Dictionary<string, int> _stringIndex = new();
    private readonly List<XlsxStyle> _styles = new() { new XlsxStyle(Size: 11, Color: "000000", Valign: "") };
    private readonly Dictionary<XlsxStyle, int> _styleIndex = new();

    public string Title { get; set; } = "";
    public string Author { get; set; } = "";

    public XlsxSheet AddSheet(string name)
    {
        var clean = new string(name.Where(c => !"\\/?*[]:".Contains(c)).ToArray());
        if (clean.Length > 31) clean = clean.Substring(0, 31);
        var sheet = new XlsxSheet(clean);
        _sheets.Add(sheet);
        return sheet;
    }

    internal int StyleId(XlsxStyle? style)
    {
        if (style == null) return 0;
        if (_styleIndex.TryGetValue(style, out var id)) return id;
        _styles.Add(style);
        return _styleIndex[style] = _styles.Count - 1;
    }

    internal int StringId(string s)
    {
        if (s.Length > 32000) s = s.Substring(0, 32000);
        if (_stringIndex.TryGetValue(s, out var id)) return id;
        _strings.Add(s);
        return _stringIndex[s] = _strings.Count - 1;
    }

    internal static string Number(object? v) => v switch
    {
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        double d => double.IsFinite(d) ? d.ToString("R", CultureInfo.InvariantCulture) : "0",
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "0"
    };

    /// <summary>Escapes text for XML and drops the control characters XML cannot hold.</summary>
    internal static string Esc(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c < 0x20 && c != '\t' && c != '\n' && c != '\r') continue;
            if (c == 0xFFFE || c == 0xFFFF) continue;
            if (char.IsHighSurrogate(c)) { if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[i + 1]); i++; } continue; }
            if (char.IsLowSurrogate(c)) continue;
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    public byte[] Build()
    {
        if (_sheets.Count == 0) AddSheet("Report");
        var entries = new List<(string Path, string Xml)>();
        var inv = CultureInfo.InvariantCulture;

        // Charts and drawings: one drawing part per sheet that has charts.
        var chartNumber = 0;
        var drawingBySheet = new Dictionary<int, int>();
        var drawingCount = 0;
        for (var si = 0; si < _sheets.Count; si++)
        {
            var sheet = _sheets[si];
            if (sheet.Charts.Count == 0) continue;
            drawingCount++;
            drawingBySheet[si] = drawingCount;
            var dr = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">");
            var rels = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            var local = 0;
            foreach (var chart in sheet.Charts)
            {
                chartNumber++; local++;
                entries.Add(("xl/charts/chart" + chartNumber + ".xml", ChartXml(chart)));
                rels.Append("<Relationship Id=\"rId").Append(local).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart\" Target=\"../charts/chart").Append(chartNumber).Append(".xml\"/>");
                dr.Append("<xdr:twoCellAnchor><xdr:from><xdr:col>").Append(chart.FromColumn - 1).Append("</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>").Append(chart.FromRow - 1)
                  .Append("</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:to><xdr:col>").Append(chart.ToColumn - 1).Append("</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>").Append(chart.ToRow - 1)
                  .Append("</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to><xdr:graphicFrame macro=\"\"><xdr:nvGraphicFramePr><xdr:cNvPr id=\"").Append(local + 1).Append("\" name=\"Chart ").Append(local)
                  .Append("\"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr><xdr:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/></xdr:xfrm><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\">")
                  .Append("<c:chart xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" r:id=\"rId").Append(local)
                  .Append("\"/></a:graphicData></a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:twoCellAnchor>");
            }
            dr.Append("</xdr:wsDr>");
            rels.Append("</Relationships>");
            entries.Add(("xl/drawings/drawing" + drawingCount + ".xml", dr.ToString()));
            entries.Add(("xl/drawings/_rels/drawing" + drawingCount + ".xml.rels", rels.ToString()));
            entries.Add(("xl/worksheets/_rels/sheet" + (si + 1) + ".xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing\" Target=\"../drawings/drawing" + drawingCount + ".xml\"/></Relationships>"));
        }

        // Sheets (this also fills the shared string and style tables).
        for (var si = 0; si < _sheets.Count; si++)
            entries.Add(("xl/worksheets/sheet" + (si + 1) + ".xml", _sheets[si].BuildXml(this, si == 0, drawingBySheet.ContainsKey(si))));

        entries.Add(("xl/styles.xml", StylesXml()));

        var ss = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"" + _strings.Count + "\" uniqueCount=\"" + _strings.Count + "\">");
        foreach (var s in _strings)
        {
            var keep = s.Length > 0 && (char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[s.Length - 1]) || s.Contains('\n'));
            ss.Append(keep ? "<si><t xml:space=\"preserve\">" : "<si><t>").Append(Esc(s)).Append("</t></si>");
        }
        ss.Append("</sst>");
        entries.Add(("xl/sharedStrings.xml", ss.ToString()));

        // Workbook.
        var wb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><workbookPr/><bookViews><workbookView xWindow=\"240\" yWindow=\"15\" windowWidth=\"22000\" windowHeight=\"12000\"/></bookViews><sheets>");
        for (var si = 0; si < _sheets.Count; si++)
            wb.Append("<sheet name=\"").Append(Esc(_sheets[si].Name)).Append("\" sheetId=\"").Append(si + 1).Append("\" r:id=\"rId").Append(si + 1).Append("\"/>");
        wb.Append("</sheets>");
        var names = new StringBuilder();
        for (var si = 0; si < _sheets.Count; si++)
            if (_sheets[si].FilterRange is { } range)
                names.Append("<definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"").Append(si).Append("\" hidden=\"1\">").Append(Esc(range)).Append("</definedName>");
        if (names.Length > 0) wb.Append("<definedNames>").Append(names).Append("</definedNames>");
        wb.Append("<calcPr calcId=\"124519\" fullCalcOnLoad=\"1\"/></workbook>");
        entries.Add(("xl/workbook.xml", wb.ToString()));

        var wr = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (var si = 0; si < _sheets.Count; si++)
            wr.Append("<Relationship Id=\"rId").Append(si + 1).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet").Append(si + 1).Append(".xml\"/>");
        wr.Append("<Relationship Id=\"rId").Append(_sheets.Count + 1).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
        wr.Append("<Relationship Id=\"rId").Append(_sheets.Count + 2).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/></Relationships>");
        entries.Add(("xl/_rels/workbook.xml.rels", wr.ToString()));

        entries.Add(("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
            "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/></Relationships>"));
        entries.Add(("docProps/core.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
            "<dc:title>" + Esc(Title) + "</dc:title><dc:creator>" + Esc(Author) + "</dc:creator><dcterms:created xsi:type=\"dcterms:W3CDTF\">" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", inv) + "</dcterms:created></cp:coreProperties>"));
        entries.Add(("docProps/app.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"><Application>RROMS</Application></Properties>"));

        var ct = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        ct.Append("<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/><Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>");
        ct.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/><Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>");
        for (var si = 0; si < _sheets.Count; si++)
            ct.Append("<Override PartName=\"/xl/worksheets/sheet").Append(si + 1).Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        for (var n = 1; n <= drawingCount; n++)
            ct.Append("<Override PartName=\"/xl/drawings/drawing").Append(n).Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/>");
        for (var n = 1; n <= chartNumber; n++)
            ct.Append("<Override PartName=\"/xl/charts/chart").Append(n).Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawingml.chart+xml\"/>");
        ct.Append("</Types>");

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string xml)
            {
                var e = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var w = e.Open();
                var bytes = new UTF8Encoding(false).GetBytes(xml);
                w.Write(bytes, 0, bytes.Length);
            }
            Add("[Content_Types].xml", ct.ToString());
            foreach (var (path, xml) in entries) Add(path, xml);
        }
        return ms.ToArray();
    }

    // ------------------------------------------------------------------ styles

    private static readonly Dictionary<string, int> BuiltInFormats = new()
    {
        ["General"] = 0, ["0"] = 1, ["0.00"] = 2, ["#,##0"] = 3, ["#,##0.00"] = 4, ["0%"] = 9, ["0.00%"] = 10
    };

    private string StylesXml()
    {
        var fonts = new List<string>();
        var fills = new List<string>();
        var borders = new List<string> { "<border><left/><right/><top/><bottom/><diagonal/></border>" };
        var numFmts = new List<string>();
        var xfs = new StringBuilder();

        int Index(List<string> list, string xml) { var i = list.IndexOf(xml); if (i >= 0) return i; list.Add(xml); return list.Count - 1; }
        string Argb(string hex) => "FF" + hex.TrimStart('#').ToUpperInvariant();

        foreach (var s in _styles)
        {
            var font = "<font>" + (s.Bold ? "<b/>" : "") + (s.Italic ? "<i/>" : "") + "<sz val=\"" + s.Size.ToString("0.##", CultureInfo.InvariantCulture) + "\"/><color rgb=\"" + Argb(s.Color) + "\"/><name val=\"" + Esc(s.Font) + "\"/><family val=\"2\"/></font>";
            var fontId = Index(fonts, font);
            var fillId = 0;
            if (s.Fill != null) fillId = Index(fills, "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"" + Argb(s.Fill) + "\"/><bgColor indexed=\"64\"/></patternFill></fill>") + 2;
            var borderId = 0;
            if (s.Border != "")
            {
                var line = "<color rgb=\"" + Argb(s.BorderColor) + "\"/>";
                string Side(string name, bool on) => on ? "<" + name + " style=\"thin\">" + line + "</" + name + ">" : "<" + name + "/>";
                var box = s.Border == "box";
                borderId = Index(borders, "<border>" + Side("left", box) + Side("right", box) + Side("top", box || s.Border == "top") + Side("bottom", box || s.Border == "bottom") + "<diagonal/></border>");
            }
            var numId = 0;
            if (!string.IsNullOrEmpty(s.NumFmt))
            {
                if (!BuiltInFormats.TryGetValue(s.NumFmt, out numId))
                {
                    numId = 164 + Index(numFmts, s.NumFmt);
                }
            }
            xfs.Append("<xf numFmtId=\"").Append(numId).Append("\" fontId=\"").Append(fontId).Append("\" fillId=\"").Append(fillId).Append("\" borderId=\"").Append(borderId)
               .Append("\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"");
            if (s.Halign != "" || s.Valign != "" || s.Wrap || s.Indent > 0)
            {
                xfs.Append(" applyAlignment=\"1\"><alignment");
                if (s.Halign != "") xfs.Append(" horizontal=\"").Append(s.Halign).Append('"');
                if (s.Valign != "") xfs.Append(" vertical=\"").Append(s.Valign).Append('"');
                if (s.Wrap) xfs.Append(" wrapText=\"1\"");
                if (s.Indent > 0) xfs.Append(" indent=\"").Append(s.Indent).Append('"');
                xfs.Append("/></xf>");
            }
            else xfs.Append("/>");
        }

        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        if (numFmts.Count > 0)
        {
            sb.Append("<numFmts count=\"").Append(numFmts.Count).Append("\">");
            for (var i = 0; i < numFmts.Count; i++) sb.Append("<numFmt numFmtId=\"").Append(164 + i).Append("\" formatCode=\"").Append(Esc(numFmts[i])).Append("\"/>");
            sb.Append("</numFmts>");
        }
        sb.Append("<fonts count=\"").Append(fonts.Count).Append("\">").Append(string.Concat(fonts)).Append("</fonts>");
        sb.Append("<fills count=\"").Append(fills.Count + 2).Append("\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>").Append(string.Concat(fills)).Append("</fills>");
        sb.Append("<borders count=\"").Append(borders.Count).Append("\">").Append(string.Concat(borders)).Append("</borders>");
        sb.Append("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
        sb.Append("<cellXfs count=\"").Append(_styles.Count).Append("\">").Append(xfs).Append("</cellXfs>");
        sb.Append("<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles><dxfs count=\"0\"/><tableStyles count=\"0\" defaultTableStyle=\"TableStyleMedium9\" defaultPivotStyle=\"PivotStyleLight16\"/></styleSheet>");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ charts

    private const string Ink = "6B5440";

    private static string Fill(string hex) => "<a:solidFill><a:srgbClr val=\"" + hex + "\"/></a:solidFill>";

    private static string TextProps(string color, int size, bool bold = false) =>
        "<c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz=\"" + size + "\" b=\"" + (bold ? 1 : 0) + "\">" + Fill(color) + "</a:defRPr></a:pPr><a:endParaRPr lang=\"en-US\"/></a:p></c:txPr>";

    private static string ChartXml(XlsxChart ch)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
        sb.Append("<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        sb.Append("<c:lang val=\"en-US\"/><c:roundedCorners val=\"0\"/><c:chart>");
        if (ch.Title.Length > 0)
        {
            sb.Append("<c:title><c:tx><c:rich><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz=\"1300\" b=\"0\">").Append(Fill("442D1C")).Append("<a:latin typeface=\"Georgia\"/></a:defRPr></a:pPr>")
              .Append("<a:r><a:rPr lang=\"en-US\" sz=\"1300\" b=\"0\">").Append(Fill("442D1C")).Append("<a:latin typeface=\"Georgia\"/></a:rPr><a:t>").Append(Esc(ch.Title)).Append("</a:t></a:r></a:p></c:rich></c:tx><c:overlay val=\"0\"/></c:title>");
            sb.Append("<c:autoTitleDeleted val=\"0\"/>");
        }
        else sb.Append("<c:autoTitleDeleted val=\"1\"/>");

        sb.Append("<c:plotArea><c:layout/>");
        var round = ch.Kind == ChartKind.Doughnut;
        var catId = 50010001 + 0;
        var valId = 50010002;
        var horizontal = ch.Kind == ChartKind.Bar;

        switch (ch.Kind)
        {
            case ChartKind.Column:
            case ChartKind.Bar:
                sb.Append("<c:barChart><c:barDir val=\"").Append(horizontal ? "bar" : "col").Append("\"/><c:grouping val=\"clustered\"/><c:varyColors val=\"0\"/>");
                break;
            case ChartKind.Line: sb.Append("<c:lineChart><c:grouping val=\"standard\"/><c:varyColors val=\"0\"/>"); break;
            case ChartKind.Area: sb.Append("<c:areaChart><c:grouping val=\"standard\"/><c:varyColors val=\"0\"/>"); break;
            case ChartKind.Doughnut: sb.Append("<c:doughnutChart><c:varyColors val=\"1\"/>"); break;
        }

        for (var i = 0; i < ch.Series.Count; i++)
        {
            var s = ch.Series[i];
            sb.Append("<c:ser><c:idx val=\"").Append(i).Append("\"/><c:order val=\"").Append(i).Append("\"/>");
            sb.Append("<c:tx><c:strRef><c:f>").Append(Esc(s.Sheet.AbsRange(s.FirstRow - 1, s.ValueColumn, s.FirstRow - 1, s.ValueColumn))).Append("</c:f><c:strCache><c:ptCount val=\"1\"/><c:pt idx=\"0\"><c:v>").Append(Esc(s.Name)).Append("</c:v></c:pt></c:strCache></c:strRef></c:tx>");
            switch (ch.Kind)
            {
                case ChartKind.Column:
                case ChartKind.Bar:
                    sb.Append("<c:spPr>").Append(Fill(s.Color)).Append("</c:spPr><c:invertIfNegative val=\"0\"/>");
                    break;
                case ChartKind.Area:
                    sb.Append("<c:spPr>").Append(Fill(s.Color)).Append("<a:ln w=\"19050\">").Append(Fill(s.Color)).Append("</a:ln></c:spPr>");
                    break;
                case ChartKind.Line:
                    sb.Append("<c:spPr><a:ln w=\"28575\" cap=\"rnd\">").Append(Fill(s.Color)).Append("<a:round/></a:ln></c:spPr><c:marker><c:symbol val=\"circle\"/><c:size val=\"5\"/><c:spPr>").Append(Fill(s.Color)).Append("<a:ln w=\"9525\">").Append(Fill("FFFCF3")).Append("</a:ln></c:spPr></c:marker>");
                    break;
                case ChartKind.Doughnut:
                    if (s.PointColors != null)
                        for (var p = 0; p < s.PointColors.Length; p++)
                            sb.Append("<c:dPt><c:idx val=\"").Append(p).Append("\"/><c:bubble3D val=\"0\"/><c:spPr>").Append(Fill(s.PointColors[p])).Append("<a:ln w=\"19050\">").Append(Fill("FFFCF3")).Append("</a:ln></c:spPr></c:dPt>");
                    break;
            }
            if (ch.DataLabels)
            {
                sb.Append("<c:dLbls>");
                if (round) sb.Append("<c:numFmt formatCode=\"0%\" sourceLinked=\"0\"/>");
                else sb.Append("<c:numFmt formatCode=\"").Append(Esc(ch.AxisFormat)).Append("\" sourceLinked=\"0\"/>");
                sb.Append(round ? "<c:spPr>" + Fill("FFFCF3") + "<a:ln><a:noFill/></a:ln></c:spPr>" : "<c:spPr><a:noFill/><a:ln><a:noFill/></a:ln></c:spPr>").Append(TextProps("442D1C", 800, true));
                if (ch.Kind == ChartKind.Column || ch.Kind == ChartKind.Bar) sb.Append("<c:dLblPos val=\"outEnd\"/>");
                else if (ch.Kind == ChartKind.Line) sb.Append("<c:dLblPos val=\"t\"/>");
                sb.Append("<c:showLegendKey val=\"0\"/><c:showVal val=\"").Append(round ? 0 : 1).Append("\"/><c:showCatName val=\"0\"/><c:showSerName val=\"0\"/><c:showPercent val=\"").Append(round ? 1 : 0).Append("\"/><c:showBubbleSize val=\"0\"/>");
                if (round) sb.Append("<c:showLeaderLines val=\"1\"/>");
                sb.Append("</c:dLbls>");
            }

            sb.Append("<c:cat><c:strRef><c:f>").Append(Esc(s.Sheet.AbsRange(s.FirstRow, s.CategoryColumn, s.LastRow, s.CategoryColumn))).Append("</c:f><c:strCache><c:ptCount val=\"").Append(s.LastRow - s.FirstRow + 1).Append("\"/>");
            for (var r = s.FirstRow; r <= s.LastRow; r++)
                sb.Append("<c:pt idx=\"").Append(r - s.FirstRow).Append("\"><c:v>").Append(Esc(CellText(s.Sheet.Get(r, s.CategoryColumn)))).Append("</c:v></c:pt>");
            sb.Append("</c:strCache></c:strRef></c:cat>");

            sb.Append("<c:val><c:numRef><c:f>").Append(Esc(s.Sheet.AbsRange(s.FirstRow, s.ValueColumn, s.LastRow, s.ValueColumn))).Append("</c:f><c:numCache><c:formatCode>General</c:formatCode><c:ptCount val=\"").Append(s.LastRow - s.FirstRow + 1).Append("\"/>");
            for (var r = s.FirstRow; r <= s.LastRow; r++)
            {
                var v = s.Sheet.Get(r, s.ValueColumn);
                if (v is XlsxFormula f) v = f.Cached;
                if (v == null || v is string) continue;
                sb.Append("<c:pt idx=\"").Append(r - s.FirstRow).Append("\"><c:v>").Append(Number(v)).Append("</c:v></c:pt>");
            }
            sb.Append("</c:numCache></c:numRef></c:val>");
            if (ch.Kind == ChartKind.Line) sb.Append("<c:smooth val=\"0\"/>");
            sb.Append("</c:ser>");
        }

        switch (ch.Kind)
        {
            case ChartKind.Column:
            case ChartKind.Bar: sb.Append("<c:gapWidth val=\"55\"/><c:axId val=\"").Append(catId).Append("\"/><c:axId val=\"").Append(valId).Append("\"/></c:barChart>"); break;
            case ChartKind.Line: sb.Append("<c:marker val=\"1\"/><c:axId val=\"").Append(catId).Append("\"/><c:axId val=\"").Append(valId).Append("\"/></c:lineChart>"); break;
            case ChartKind.Area: sb.Append("<c:axId val=\"").Append(catId).Append("\"/><c:axId val=\"").Append(valId).Append("\"/></c:areaChart>"); break;
            case ChartKind.Doughnut: sb.Append("<c:firstSliceAng val=\"0\"/><c:holeSize val=\"55\"/></c:doughnutChart>"); break;
        }

        if (!round)
        {
            var axisLine = "<c:spPr><a:ln w=\"9525\">" + Fill("9D9167") + "</a:ln></c:spPr>";
            sb.Append("<c:catAx><c:axId val=\"").Append(catId).Append("\"/><c:scaling><c:orientation val=\"").Append(horizontal ? "maxMin" : "minMax").Append("\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"")
              .Append(horizontal ? "l" : "b").Append("\"/><c:numFmt formatCode=\"General\" sourceLinked=\"0\"/><c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/>")
              .Append(axisLine).Append(TextProps(Ink, 800)).Append("<c:crossAx val=\"").Append(valId).Append("\"/><c:crosses val=\"autoZero\"/><c:auto val=\"1\"/><c:lblAlgn val=\"ctr\"/><c:lblOffset val=\"100\"/><c:noMultiLvlLbl val=\"0\"/></c:catAx>");
            sb.Append("<c:valAx><c:axId val=\"").Append(valId).Append("\"/><c:scaling><c:orientation val=\"minMax\"/>");
            if (ch.AxisMax != null) sb.Append("<c:max val=\"").Append(ch.AxisMax.Value.ToString("R", inv)).Append("\"/>");
            if (ch.AxisMin != null) sb.Append("<c:min val=\"").Append(ch.AxisMin.Value.ToString("R", inv)).Append("\"/>");
            sb.Append("</c:scaling><c:delete val=\"0\"/><c:axPos val=\"").Append(horizontal ? "b" : "l").Append("\"/><c:majorGridlines><c:spPr><a:ln w=\"6350\">").Append(Fill("E8D1A7")).Append("</a:ln></c:spPr></c:majorGridlines>")
              .Append("<c:numFmt formatCode=\"").Append(Esc(ch.AxisFormat)).Append("\" sourceLinked=\"0\"/><c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/><c:spPr><a:ln><a:noFill/></a:ln></c:spPr>")
              .Append(TextProps(Ink, 800)).Append("<c:crossAx val=\"").Append(catId).Append("\"/><c:crosses val=\"").Append(horizontal ? "max" : "autoZero").Append("\"/><c:crossBetween val=\"")
              .Append(ch.Kind == ChartKind.Area ? "midCat" : "between").Append("\"/></c:valAx>");
        }
        sb.Append("<c:spPr><a:noFill/></c:spPr></c:plotArea>");

        if (ch.Legend)
            sb.Append("<c:legend><c:legendPos val=\"r\"/><c:overlay val=\"0\"/>").Append(TextProps(Ink, 900)).Append("</c:legend>");
        sb.Append("<c:plotVisOnly val=\"1\"/><c:dispBlanksAs val=\"gap\"/></c:chart>");
        sb.Append("<c:spPr>").Append(Fill("FFFCF3")).Append("<a:ln w=\"9525\">").Append(Fill("E8D1A7")).Append("</a:ln></c:spPr>");
        sb.Append("</c:chartSpace>");
        return sb.ToString();
    }

    private static string CellText(object? v) => v switch
    {
        null => "",
        string s => s,
        DateTime d => d.ToString("d MMM", CultureInfo.InvariantCulture),
        XlsxFormula f => f.Cached.ToString("R", CultureInfo.InvariantCulture),
        _ => Number(v)
    };
}
