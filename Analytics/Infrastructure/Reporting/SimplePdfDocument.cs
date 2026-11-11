using System.Globalization;
using System.Text;

namespace Hampcoders.Electrolink.API.Analytics.Infrastructure.Reporting;

/// <summary>
///     Minimal PDF 1.4 writer for tabular text reports: A4 pages, standard fonts (no embedding), automatic page breaks.
///     Body text uses Courier so columns built with padding stay aligned. Text is encoded as WinAnsi (Latin-1 range);
///     other characters are replaced with '?'.
/// </summary>
public sealed class SimplePdfDocument
{
    private const float PageWidth = 595f, PageHeight = 842f, Margin = 42f, BodySize = 8.5f, Leading = 11f;
    /// <summary>Characters per body line that fit between the margins (Courier advance is 0.6 em).</summary>
    public static readonly int LineWidth = (int)((PageWidth - 2 * Margin) / (BodySize * 0.6f));

    private readonly List<StringBuilder> _pages = [];
    private readonly string _footer;
    private float _y;

    public SimplePdfDocument(string footer) { _footer = footer; NewPage(); }

    public void Title(string text) { Ensure(2); Write("F2", 15f, text); _y -= 8; }
    public void Heading(string text) { Ensure(3); _y -= 6; Write("F2", 11f, text); _y -= 2; }
    public void Line(string text = "") { Ensure(1); Write("F1", BodySize, text.Length > LineWidth ? text[..LineWidth] : text); }

    private void Ensure(int lines) { if (_y - lines * Leading < Margin + Leading) NewPage(); }

    private void NewPage() { _pages.Add(new StringBuilder()); _y = PageHeight - Margin; }

    private void Write(string font, float size, string text)
    {
        _y -= size > BodySize ? size + 2 : Leading;
        _pages[^1].Append(CultureInfo.InvariantCulture, $"BT /{font} {size:0.##} Tf {Margin:0.##} {_y:0.##} Td ({Escape(text)}) Tj ET\n");
    }

    public byte[] ToBytes()
    {
        var latin1 = Encoding.Latin1;
        using var output = new MemoryStream();
        var offsets = new List<long>();
        void Raw(string s) { var b = latin1.GetBytes(s); output.Write(b, 0, b.Length); }
        void Obj(string body) { offsets.Add(output.Position); Raw($"{offsets.Count} 0 obj\n{body}\nendobj\n"); }

        Raw("%PDF-1.4\n%âãÏÓ\n");
        var pageCount = _pages.Count;
        // Object layout: 1 catalog, 2 pages, 3 body font, 4 bold font, then (page, contents) pairs.
        var kids = string.Join(' ', Enumerable.Range(0, pageCount).Select(i => $"{5 + 2 * i} 0 R"));
        Obj("<< /Type /Catalog /Pages 2 0 R >>");
        Obj($"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        for (var i = 0; i < pageCount; i++)
        {
            var footer = string.Create(CultureInfo.InvariantCulture,
                $"BT /F1 7 Tf {Margin:0.##} 24 Td ({Escape($"{_footer}  -  page {i + 1} of {pageCount}")}) Tj ET\n");
            var content = _pages[i] + footer;
            Obj(string.Create(CultureInfo.InvariantCulture,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth:0} {PageHeight:0}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {6 + 2 * i} 0 R >>"));
            Obj($"<< /Length {latin1.GetByteCount(content)} >>\nstream\n{content}endstream");
        }
        var xref = output.Position;
        var table = new StringBuilder($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) table.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        table.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Raw(table.ToString());
        return output.ToArray();
    }

    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '\\' or '(' or ')') sb.Append('\\').Append(c);
            else if (c < 32) sb.Append(' ');
            else sb.Append(c <= 255 ? c : '?');
        }
        return sb.ToString();
    }
}
