using System.Text;
using System.Text.Encodings.Web;

namespace RROMS.Presentation;

/// 
/// A small plan of the tables for the administrator's Overview: one block per dining area, each table a round (two seats) or
/// rounded (more) shape coloured by whether it is free, booked or seated. Built as a string because SVG's text element is
/// called text, which Razor reserves; every name goes through the encoder.
/// 
public static class FloorMap
{
    public record Seat(string Number, int Capacity, string State, string Sub, string Title);
    public record Zone(string Name, List<Seat> Seats);

    public static string Svg(IReadOnlyList<Zone> zones, int perRow = 3)
    {
        const int cell = 84, gap = 14, left = 14, rowH = 78;
        var enc = HtmlEncoder.Default;
        var sb = new StringBuilder();
        var y = 6;
        foreach (var z in zones)
        {
            if (z.Seats.Count == 0) continue;
            sb.Append($"<text x=\"{left}\" y=\"{y + 14}\" class=\"fl-area\">{enc.Encode(z.Name)}</text>");
            y += 24;
            for (var k = 0; k < z.Seats.Count; k++)
            {
                var s = z.Seats[k];
                var cx = left + (k % perRow) * (cell + gap) + cell / 2;
                var ty = y + (k / perRow) * rowH;
                var cy = ty + (rowH - 10) / 2;
                sb.Append($"<g class=\"fl-t fl-{s.State}\" role=\"img\" aria-label=\"{enc.Encode(s.Title)}\">");
                if (s.Capacity <= 2) sb.Append($"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{(rowH - 10) / 2}\"></circle>");
                else sb.Append($"<rect x=\"{cx - cell / 2}\" y=\"{ty}\" width=\"{cell}\" height=\"{rowH - 10}\" rx=\"12\"></rect>");
                sb.Append($"<text x=\"{cx}\" y=\"{cy - 2}\" text-anchor=\"middle\" class=\"fl-n\">{enc.Encode(s.Number)}</text>");
                sb.Append($"<text x=\"{cx}\" y=\"{cy + 15}\" text-anchor=\"middle\" class=\"fl-c\">{enc.Encode(s.Sub)}</text>");
                sb.Append("</g>");
            }
            y += (z.Seats.Count + perRow - 1) / perRow * rowH + 10;
        }
        var w = left * 2 + perRow * cell + (perRow - 1) * gap;
        return $"<svg class=\"floor floor-static\" viewBox=\"0 0 {w} {Math.Max(y, 60)}\" role=\"group\" aria-label=\"Tables and whether they are free, booked or seated\">{sb}</svg>";
    }
}
