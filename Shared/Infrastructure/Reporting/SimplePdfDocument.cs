using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Hampcoders.Electrolink.API.Shared.Infrastructure.Reporting;

/// <summary>
///     Minimal PDF 1.4 writer for tabular text reports: A4 pages, standard fonts (no embedding), automatic page breaks,
///     and raster images (e.g. a handwritten signature). Body text uses Courier so columns built with padding stay
///     aligned. Text is encoded as WinAnsi (Latin-1 range); other characters are replaced with '?'.
/// </summary>
public sealed class SimplePdfDocument
{
    private const float PageWidth = 595f, PageHeight = 842f, Margin = 42f, BodySize = 8.5f, Leading = 11f;
    /// <summary>Characters per body line that fit between the margins (Courier advance is 0.6 em).</summary>
    public static readonly int LineWidth = (int)((PageWidth - 2 * Margin) / (BodySize * 0.6f));

    private readonly List<StringBuilder> _pages = [];
    private readonly List<HashSet<int>> _pageImages = [];
    private readonly List<RasterImage> _images = [];
    private readonly string _footer;
    private float _y;

    public SimplePdfDocument(string footer) { _footer = footer; NewPage(); }

    public void Title(string text) { Ensure(2); Write("F2", 15f, text); _y -= 8; }
    public void Heading(string text) { Ensure(3); _y -= 6; Write("F2", 11f, text); _y -= 2; }
    public void Line(string text = "") { Ensure(1); Write("F1", BodySize, text.Length > LineWidth ? text[..LineWidth] : text); }

    /// <summary>Draws the image at the left margin, scaled to <paramref name="maxWidth"/> points wide (aspect ratio kept).</summary>
    public void Image(RasterImage image, float maxWidth, float maxHeight = 120f)
    {
        var scale = Math.Min(maxWidth / image.Width, maxHeight / image.Height);
        var width = image.Width * scale;
        var height = image.Height * scale;
        if (_y - height < Margin + Leading) NewPage();
        var index = _images.IndexOf(image);
        if (index < 0) { _images.Add(image); index = _images.Count - 1; }
        _pageImages[^1].Add(index);
        _y -= height + 4;
        _pages[^1].Append(CultureInfo.InvariantCulture,
            $"q {width:0.##} 0 0 {height:0.##} {Margin:0.##} {_y:0.##} cm /Im{index + 1} Do Q\n");
    }

    private void Ensure(int lines) { if (_y - lines * Leading < Margin + Leading) NewPage(); }

    private void NewPage() { _pages.Add(new StringBuilder()); _pageImages.Add([]); _y = PageHeight - Margin; }

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
        void StreamObj(string dictionary, byte[] data)
        {
            offsets.Add(output.Position);
            Raw($"{offsets.Count} 0 obj\n{dictionary}\nstream\n");
            output.Write(data, 0, data.Length);
            Raw("\nendstream\nendobj\n");
        }

        Raw("%PDF-1.4\n%âãÏÓ\n");
        var pageCount = _pages.Count;
        // Object layout: 1 catalog, 2 pages, 3 body font, 4 bold font, then images (each with its alpha mask), then (page, contents) pairs.
        var imageObject = new int[_images.Count];
        var next = 5;
        for (var i = 0; i < _images.Count; i++) { imageObject[i] = next; next += _images[i].Alpha == null ? 1 : 2; }
        var firstPage = next;
        var kids = string.Join(' ', Enumerable.Range(0, pageCount).Select(i => $"{firstPage + 2 * i} 0 R"));
        Obj("<< /Type /Catalog /Pages 2 0 R >>");
        Obj($"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        for (var i = 0; i < _images.Count; i++)
        {
            var image = _images[i];
            var color = Deflate(image.Rgb);
            var mask = image.Alpha == null ? "" : $" /SMask {imageObject[i] + 1} 0 R";
            StreamObj($"<< /Type /XObject /Subtype /Image /Width {image.Width} /Height {image.Height} /ColorSpace /DeviceRGB " +
                      $"/BitsPerComponent 8 /Filter /FlateDecode{mask} /Length {color.Length} >>", color);
            if (image.Alpha == null) continue;
            var alpha = Deflate(image.Alpha);
            StreamObj($"<< /Type /XObject /Subtype /Image /Width {image.Width} /Height {image.Height} /ColorSpace /DeviceGray " +
                      $"/BitsPerComponent 8 /Filter /FlateDecode /Length {alpha.Length} >>", alpha);
        }
        for (var i = 0; i < pageCount; i++)
        {
            var footer = string.Create(CultureInfo.InvariantCulture,
                $"BT /F1 7 Tf {Margin:0.##} 24 Td ({Escape($"{_footer}  -  page {i + 1} of {pageCount}")}) Tj ET\n");
            var content = _pages[i] + footer;
            var xObjects = _pageImages[i].Count == 0 ? ""
                : " /XObject << " + string.Join(' ', _pageImages[i].Order().Select(k => $"/Im{k + 1} {imageObject[k]} 0 R")) + " >>";
            Obj(string.Create(CultureInfo.InvariantCulture,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth:0} {PageHeight:0}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >>{xObjects} >> /Contents {firstPage + 2 * i + 1} 0 R >>"));
            Obj($"<< /Length {latin1.GetByteCount(content)} >>\nstream\n{content}endstream");
        }
        var xref = output.Position;
        var table = new StringBuilder($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) table.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        table.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Raw(table.ToString());
        return output.ToArray();
    }

    private static byte[] Deflate(byte[] data)
    {
        using var buffer = new MemoryStream();
        using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(data, 0, data.Length);
        return buffer.ToArray();
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

/// <summary>Decoded raster image: 8-bit RGB samples, plus an optional 8-bit alpha channel.</summary>
public sealed class RasterImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Rgb { get; }
    public byte[]? Alpha { get; }

    private RasterImage(int width, int height, byte[] rgb, byte[]? alpha) { Width = width; Height = height; Rgb = rgb; Alpha = alpha; }

    /// <summary>
    ///     Decodes a non-interlaced, 8-bit PNG (grayscale, RGB, palette, grayscale+alpha or RGBA), which covers what
    ///     browser canvases export. Throws <see cref="ArgumentException"/> for anything else.
    /// </summary>
    public static RasterImage FromPng(byte[] png, int maxPixels = 4_000_000)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (png.Length < 33 || !png.AsSpan(0, 8).SequenceEqual(signature)) throw new ArgumentException("The image is not a PNG file.");
        int width = 0, height = 0, colorType = -1;
        byte[]? palette = null, paletteAlpha = null;
        using var idat = new MemoryStream();
        var position = 8;
        while (position + 12 <= png.Length)
        {
            var length = ReadInt(png, position);
            var type = Encoding.ASCII.GetString(png, position + 4, 4);
            if (length < 0 || position + 12 + length > png.Length) throw new ArgumentException("The PNG file is truncated.");
            var data = png.AsSpan(position + 8, length);
            switch (type)
            {
                case "IHDR":
                    width = ReadInt(png, position + 8); height = ReadInt(png, position + 12);
                    var bitDepth = data[8]; colorType = data[9];
                    if (bitDepth != 8 || data[12] != 0 || colorType is not (0 or 2 or 3 or 4 or 6))
                        throw new ArgumentException("Only non-interlaced 8-bit PNG images are supported.");
                    if (width <= 0 || height <= 0 || (long)width * height > maxPixels) throw new ArgumentException("The PNG dimensions are not allowed.");
                    break;
                case "PLTE": palette = data.ToArray(); break;
                case "tRNS": paletteAlpha = data.ToArray(); break;
                case "IDAT": idat.Write(data); break;
                case "IEND": position = png.Length; continue;
            }
            position += 12 + length;
        }
        if (colorType < 0 || idat.Length == 0) throw new ArgumentException("The PNG file has no image data.");
        if (colorType == 3 && palette == null) throw new ArgumentException("The PNG palette is missing.");

        var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        var stride = width * channels;
        var raw = new byte[(long)(stride + 1) * height];
        idat.Position = 0;
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress))
        {
            var read = 0;
            while (read < raw.Length)
            {
                var n = zlib.Read(raw, read, raw.Length - read);
                if (n == 0) throw new ArgumentException("The PNG image data is truncated.");
                read += n;
            }
        }

        var pixels = Unfilter(raw, stride, height, channels);
        var rgb = new byte[width * height * 3];
        var alpha = colorType is 4 or 6 || (colorType == 3 && paletteAlpha != null) ? new byte[width * height] : null;
        for (var i = 0; i < width * height; i++)
        {
            var p = i * channels;
            switch (colorType)
            {
                case 0: rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = pixels[p]; break;
                case 4: rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = pixels[p]; alpha![i] = pixels[p + 1]; break;
                case 2: Buffer.BlockCopy(pixels, p, rgb, i * 3, 3); break;
                case 6: Buffer.BlockCopy(pixels, p, rgb, i * 3, 3); alpha![i] = pixels[p + 3]; break;
                default:
                    var entry = pixels[p];
                    if (entry * 3 + 2 >= palette!.Length) throw new ArgumentException("The PNG palette index is out of range.");
                    Buffer.BlockCopy(palette, entry * 3, rgb, i * 3, 3);
                    if (alpha != null) alpha[i] = entry < paletteAlpha!.Length ? paletteAlpha[entry] : (byte)255;
                    break;
            }
        }
        return new RasterImage(width, height, rgb, alpha);
    }

    private static byte[] Unfilter(byte[] raw, int stride, int height, int bpp)
    {
        var result = new byte[stride * height];
        for (var row = 0; row < height; row++)
        {
            var filter = raw[row * (stride + 1)];
            var src = row * (stride + 1) + 1;
            var dst = row * stride;
            for (var x = 0; x < stride; x++)
            {
                int left = x >= bpp ? result[dst + x - bpp] : 0;
                int up = row > 0 ? result[dst - stride + x] : 0;
                int upLeft = row > 0 && x >= bpp ? result[dst - stride + x - bpp] : 0;
                var value = raw[src + x];
                result[dst + x] = filter switch
                {
                    0 => value,
                    1 => (byte)(value + left),
                    2 => (byte)(value + up),
                    3 => (byte)(value + (left + up) / 2),
                    4 => (byte)(value + Paeth(left, up, upLeft)),
                    _ => throw new ArgumentException("The PNG uses an unknown filter.")
                };
            }
        }
        return result;
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static int ReadInt(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}
