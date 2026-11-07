using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace RROMS.Services.Reporting;

/// <summary>A colour as red, green and blue between 0 and 1 (the way a PDF wants it).</summary>
internal readonly struct Rgb
{
    public readonly double R, G, B;
    public Rgb(double r, double g, double b) { R = r; G = g; B = b; }

    public static Rgb Hex(string hex)
    {
        hex = hex.TrimStart('#');
        return new Rgb(
            Convert.ToInt32(hex.Substring(0, 2), 16) / 255.0,
            Convert.ToInt32(hex.Substring(2, 2), 16) / 255.0,
            Convert.ToInt32(hex.Substring(4, 2), 16) / 255.0);
    }

    /// <summary>t = 0 gives this colour, t = 1 gives the other one. Used for soft tints instead of transparency.</summary>
    public Rgb Mix(Rgb other, double t) => new(R + (other.R - R) * t, G + (other.G - G) * t, B + (other.B - B) * t);
}

internal enum TextAlign { Left, Center, Right }

/// <summary>
/// One page of the report. Everything is drawn with the top-left corner as (0, 0) and y growing downwards, in points
/// (1/72 inch), which is how a page is laid out on screen. The PDF's own bottom-left origin is handled in here.
/// </summary>
internal sealed class PdfPage
{
    private readonly StringBuilder _ops = new();

    public double Width { get; }
    public double Height { get; }

    internal PdfPage(double width, double height)
    {
        Width = width;
        Height = height;
        _ops.Append("1 j 1 J\n"); // round joins and caps: lines and bar ends look soft
    }

    internal string Content => _ops.ToString();

    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private string P(double x, double y) => N(x) + " " + N(Height - y);

    private void SetFill(Rgb c) => _ops.Append(N(c.R)).Append(' ').Append(N(c.G)).Append(' ').Append(N(c.B)).Append(" rg\n");
    private void SetStroke(Rgb c) => _ops.Append(N(c.R)).Append(' ').Append(N(c.G)).Append(' ').Append(N(c.B)).Append(" RG\n");

    private void Paint(bool fill, bool stroke) => _ops.Append(fill && stroke ? "B\n" : fill ? "f\n" : stroke ? "S\n" : "n\n");

    /// <summary>A rectangle, with optional rounded corners, fill and outline.</summary>
    public void Rect(double x, double y, double w, double h, Rgb? fill, Rgb? stroke = null, double lineWidth = 0.75, double radius = 0)
    {
        if (w <= 0 || h <= 0) return;
        if (fill == null && stroke == null) return;
        if (fill != null) SetFill(fill.Value);
        if (stroke != null) { SetStroke(stroke.Value); _ops.Append(N(lineWidth)).Append(" w\n"); }

        var r = Math.Min(radius, Math.Min(w, h) / 2);
        if (r <= 0.01)
        {
            _ops.Append(N(x)).Append(' ').Append(N(Height - y - h)).Append(' ').Append(N(w)).Append(' ').Append(N(h)).Append(" re\n");
        }
        else
        {
            const double k = 0.5523;
            double c = k * r;
            _ops.Append(P(x + r, y)).Append(" m\n");
            _ops.Append(P(x + w - r, y)).Append(" l\n");
            _ops.Append(P(x + w - r + c, y)).Append(' ').Append(P(x + w, y + r - c)).Append(' ').Append(P(x + w, y + r)).Append(" c\n");
            _ops.Append(P(x + w, y + h - r)).Append(" l\n");
            _ops.Append(P(x + w, y + h - r + c)).Append(' ').Append(P(x + w - r + c, y + h)).Append(' ').Append(P(x + w - r, y + h)).Append(" c\n");
            _ops.Append(P(x + r, y + h)).Append(" l\n");
            _ops.Append(P(x + r - c, y + h)).Append(' ').Append(P(x, y + h - r + c)).Append(' ').Append(P(x, y + h - r)).Append(" c\n");
            _ops.Append(P(x, y + r)).Append(" l\n");
            _ops.Append(P(x, y + r - c)).Append(' ').Append(P(x + r - c, y)).Append(' ').Append(P(x + r, y)).Append(" c\nh\n");
        }
        Paint(fill != null, stroke != null);
    }

    public void Line(double x1, double y1, double x2, double y2, Rgb color, double width = 0.75, double dash = 0)
    {
        SetStroke(color);
        _ops.Append(N(width)).Append(" w\n");
        if (dash > 0) _ops.Append('[').Append(N(dash)).Append(' ').Append(N(dash)).Append("] 0 d\n");
        _ops.Append(P(x1, y1)).Append(" m ").Append(P(x2, y2)).Append(" l S\n");
        if (dash > 0) _ops.Append("[] 0 d\n");
    }

    public void Polyline(IList<(double X, double Y)> points, Rgb color, double width = 1.5)
    {
        if (points.Count < 2) return;
        SetStroke(color);
        _ops.Append(N(width)).Append(" w\n");
        _ops.Append(P(points[0].X, points[0].Y)).Append(" m\n");
        for (var i = 1; i < points.Count; i++) _ops.Append(P(points[i].X, points[i].Y)).Append(" l\n");
        _ops.Append("S\n");
    }

    public void Polygon(IList<(double X, double Y)> points, Rgb? fill, Rgb? stroke = null, double lineWidth = 0.75)
    {
        if (points.Count < 3) return;
        if (fill != null) SetFill(fill.Value);
        if (stroke != null) { SetStroke(stroke.Value); _ops.Append(N(lineWidth)).Append(" w\n"); }
        _ops.Append(P(points[0].X, points[0].Y)).Append(" m\n");
        for (var i = 1; i < points.Count; i++) _ops.Append(P(points[i].X, points[i].Y)).Append(" l\n");
        _ops.Append("h\n");
        Paint(fill != null, stroke != null);
    }

    public void Circle(double cx, double cy, double r, Rgb? fill, Rgb? stroke = null, double lineWidth = 0.75)
    {
        const double k = 0.5523;
        double c = k * r;
        if (fill != null) SetFill(fill.Value);
        if (stroke != null) { SetStroke(stroke.Value); _ops.Append(N(lineWidth)).Append(" w\n"); }
        _ops.Append(P(cx + r, cy)).Append(" m\n");
        _ops.Append(P(cx + r, cy + c)).Append(' ').Append(P(cx + c, cy + r)).Append(' ').Append(P(cx, cy + r)).Append(" c\n");
        _ops.Append(P(cx - c, cy + r)).Append(' ').Append(P(cx - r, cy + c)).Append(' ').Append(P(cx - r, cy)).Append(" c\n");
        _ops.Append(P(cx - r, cy - c)).Append(' ').Append(P(cx - c, cy - r)).Append(' ').Append(P(cx, cy - r)).Append(" c\n");
        _ops.Append(P(cx + c, cy - r)).Append(' ').Append(P(cx + r, cy - c)).Append(' ').Append(P(cx + r, cy)).Append(" c\nh\n");
        Paint(fill != null, stroke != null);
    }

    /// <summary>One slice of a doughnut chart. Angles are in degrees, clockwise from twelve o'clock.</summary>
    public void Slice(double cx, double cy, double outer, double inner, double fromDeg, double toDeg, Rgb fill, Rgb gap)
    {
        if (toDeg - fromDeg >= 359.99)
        {
            Slice(cx, cy, outer, inner, fromDeg, fromDeg + 180, fill, fill);
            Slice(cx, cy, outer, inner, fromDeg + 180, toDeg, fill, fill);
            return;
        }
        var steps = Math.Max(2, (int)Math.Ceiling((toDeg - fromDeg) / 3.0));
        var pts = new List<(double X, double Y)>();
        for (var i = 0; i <= steps; i++)
        {
            var a = (fromDeg + (toDeg - fromDeg) * i / steps) * Math.PI / 180.0;
            pts.Add((cx + outer * Math.Sin(a), cy - outer * Math.Cos(a)));
        }
        for (var i = steps; i >= 0; i--)
        {
            var a = (fromDeg + (toDeg - fromDeg) * i / steps) * Math.PI / 180.0;
            pts.Add((cx + inner * Math.Sin(a), cy - inner * Math.Cos(a)));
        }
        Polygon(pts, fill, gap, 1.2);
    }

    /// <summary>A five-pointed star (for ratings).</summary>
    public void Star(double cx, double cy, double r, Rgb fill)
    {
        var pts = new List<(double X, double Y)>();
        for (var i = 0; i < 10; i++)
        {
            var rr = i % 2 == 0 ? r : r * 0.4;
            var a = i * Math.PI / 5.0;
            pts.Add((cx + rr * Math.Sin(a), cy - rr * Math.Cos(a)));
        }
        Polygon(pts, fill);
    }

    /// <summary>Text with y as the top of the line. Returns the width drawn.</summary>
    public double Text(double x, double y, string text, int font, double size, Rgb color, TextAlign align = TextAlign.Left)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var bytes = PdfText.Encode(text);
        var width = PdfText.Width(bytes, font, size);
        if (align == TextAlign.Center) x -= width / 2;
        else if (align == TextAlign.Right) x -= width;
        SetFill(color);
        _ops.Append("BT /").Append(PdfFonts.ResourceName[font]).Append(' ').Append(N(size)).Append(" Tf ")
            .Append(P(x, y + size * 0.8)).Append(" Td (");
        foreach (var b in bytes)
        {
            if (b == '\\' || b == '(' || b == ')') _ops.Append('\\').Append((char)b);
            else if (b < 32 || b > 126) _ops.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            else _ops.Append((char)b);
        }
        _ops.Append(") Tj ET\n");
        return width;
    }
}

/// <summary>Turning text into the single-byte codes the standard PDF fonts use, and measuring it.</summary>
internal static class PdfText
{
    public static byte[] Encode(string text)
    {
        var bytes = new List<byte>(text.Length);
        foreach (var c in text)
        {
            if (c >= 32 && c < 127) bytes.Add((byte)c);
            else if (c >= 160 && c <= 255) bytes.Add((byte)c);
            else bytes.Add(c switch
            {
                '–' => 0x96, '—' => 0x97, '‘' => 0x91, '’' => 0x92, '“' => 0x93, '”' => 0x94,
                '•' => 0x95, '…' => 0x85, '€' => 0x80, '™' => 0x99, '\t' => 32, '\n' => 32, '\r' => 32,
                _ => (byte)'?'
            });
        }
        return bytes.ToArray();
    }

    public static double Width(byte[] bytes, int font, double size)
    {
        double w = 0;
        foreach (var b in bytes) w += PdfFonts.CharWidth(font, b, size);
        return w;
    }

    public static double Measure(string text, int font, double size) => string.IsNullOrEmpty(text) ? 0 : Width(Encode(text), font, size);

    /// <summary>Shortens text with an ellipsis until it fits the width.</summary>
    public static string Fit(string text, int font, double size, double maxWidth)
    {
        text ??= "";
        if (Measure(text, font, size) <= maxWidth) return text;
        var n = text.Length;
        while (n > 1)
        {
            n--;
            var candidate = text.Substring(0, n).TrimEnd() + "…";
            if (Measure(candidate, font, size) <= maxWidth) return candidate;
        }
        return "…";
    }

    /// <summary>Breaks text into lines no wider than maxWidth; the last allowed line ends in an ellipsis if text is left over.</summary>
    public static List<string> Wrap(string text, int font, double size, double maxWidth, int maxLines)
    {
        var lines = new List<string>();
        var words = (text ?? "").Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var current = "";
        for (var i = 0; i < words.Length; i++)
        {
            var trial = current.Length == 0 ? words[i] : current + " " + words[i];
            if (Measure(trial, font, size) <= maxWidth) { current = trial; continue; }
            if (current.Length > 0)
            {
                if (lines.Count == maxLines - 1)
                {
                    lines.Add(Fit(current + " " + string.Join(" ", words.Skip(i)), font, size, maxWidth));
                    return lines;
                }
                lines.Add(current);
            }
            current = Measure(words[i], font, size) <= maxWidth ? words[i] : Fit(words[i], font, size, maxWidth);
        }
        if (current.Length > 0) lines.Add(current);
        return lines;
    }
}

/// <summary>
/// Writes a finished PDF file. This is a small writer of our own (pages of lines, shapes and text in built-in fonts),
/// so the report needs no PDF library and looks the same on every machine.
/// </summary>
internal sealed class PdfDocument
{
    public const double A4Width = 595.28, A4Height = 841.89;
    private readonly List<PdfPage> _pages = new();

    public IReadOnlyList<PdfPage> Pages => _pages;

    public PdfPage AddPage()
    {
        var page = new PdfPage(A4Width, A4Height);
        _pages.Add(page);
        return page;
    }

    public byte[] Build(string title, string author)
    {
        using var ms = new MemoryStream();
        var offsets = new Dictionary<int, long>();

        void Raw(string s) { var b = Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }
        void Begin(int id) { offsets[id] = ms.Position; Raw(id + " 0 obj\n"); }
        void End() => Raw("endobj\n");
        string Utf16(string s) => "<FEFF" + string.Concat(Encoding.BigEndianUnicode.GetBytes(s).Select(b => b.ToString("X2"))) + ">";

        Raw("%PDF-1.4\n");
        ms.Write(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' }, 0, 6);

        const int fontFirst = 3, info = 8, firstPage = 9;
        var pageIds = Enumerable.Range(0, _pages.Count).Select(i => firstPage + i * 2 + 1).ToList();

        Begin(1);
        Raw("<< /Type /Catalog /Pages 2 0 R /Lang (en-GB) /ViewerPreferences << /DisplayDocTitle true >> >>\n");
        End();

        Begin(2);
        Raw("<< /Type /Pages /Count " + _pages.Count + " /Kids [" + string.Join(" ", pageIds.Select(id => id + " 0 R")) + "] >>\n");
        End();

        for (var f = 0; f < PdfFonts.BaseFont.Length; f++)
        {
            Begin(fontFirst + f);
            Raw("<< /Type /Font /Subtype /Type1 /BaseFont /" + PdfFonts.BaseFont[f] + " /Encoding /WinAnsiEncoding >>\n");
            End();
        }

        Begin(info);
        Raw("<< /Title " + Utf16(title) + " /Author " + Utf16(author) + " /Creator " + Utf16(author) + " /Producer " + Utf16("RROMS") +
            " /CreationDate (D:" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "Z) >>\n");
        End();

        var fonts = string.Join(" ", Enumerable.Range(0, PdfFonts.BaseFont.Length).Select(f => "/" + PdfFonts.ResourceName[f] + " " + (fontFirst + f) + " 0 R"));
        for (var i = 0; i < _pages.Count; i++)
        {
            var contentId = firstPage + i * 2;
            var pageId = contentId + 1;
            byte[] raw = Encoding.ASCII.GetBytes(_pages[i].Content);
            byte[] packed;
            using (var packedStream = new MemoryStream())
            {
                using (var z = new ZLibStream(packedStream, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw, 0, raw.Length);
                packed = packedStream.ToArray();
            }

            Begin(contentId);
            Raw("<< /Length " + packed.Length + " /Filter /FlateDecode >>\nstream\n");
            ms.Write(packed, 0, packed.Length);
            Raw("\nendstream\n");
            End();

            Begin(pageId);
            Raw("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + _pages[i].Width.ToString("0.##", CultureInfo.InvariantCulture) + " " +
                _pages[i].Height.ToString("0.##", CultureInfo.InvariantCulture) + "] /Contents " + contentId + " 0 R /Resources << /Font << " + fonts + " >> >> >>\n");
            End();
        }

        var count = firstPage + _pages.Count * 2;
        var xref = ms.Position;
        Raw("xref\n0 " + count + "\n0000000000 65535 f \n");
        for (var id = 1; id < count; id++) Raw(offsets[id].ToString("D10") + " 00000 n \n");
        Raw("trailer\n<< /Size " + count + " /Root 1 0 R /Info " + info + " 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
        return ms.ToArray();
    }
}
