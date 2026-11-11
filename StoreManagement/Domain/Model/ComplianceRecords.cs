using System.Security.Cryptography;

namespace Hampcoders.Electrolink.API.StoreManagement.Domain.Model;

/// <summary>
///     Handwritten signature of the store manager (PNG), embedded in official compliance documents. A store keeps its
///     signature history; the most recent one is used.
/// </summary>
public sealed class ComplianceSignature
{
    public const int MaxImageBytes = 256 * 1024;

    public string Id { get; private set; } = null!;
    public string StoreId { get; private set; } = null!;
    public string SignerName { get; private set; } = null!;
    public string SignerTitle { get; private set; } = null!;
    public byte[] ImagePng { get; private set; } = [];
    public string Sha256 { get; private set; } = null!;
    public int WidthPx { get; private set; }
    public int HeightPx { get; private set; }
    public DateTime SignedAtUtc { get; private set; }
    public string SignedByUserId { get; private set; } = null!;
    private ComplianceSignature() { }

    /// <param name="widthPx">Dimensions of the decoded image (the caller decodes it to prove it is a usable PNG).</param>
    public static ComplianceSignature Register(string storeId, string signerName, string signerTitle, byte[] imagePng, int widthPx, int heightPx,
        string actor, DateTime nowUtc)
    {
        if (imagePng == null || imagePng.Length == 0 || imagePng.Length > MaxImageBytes)
            throw new ArgumentException($"The signature image must be a PNG of at most {MaxImageBytes / 1024} KB.");
        if (widthPx is < 50 or > 2000 || heightPx is < 20 or > 1000)
            throw new ArgumentException("The signature image must be between 50x20 and 2000x1000 pixels.");
        return new()
        {
            Id = "signature-" + Guid.NewGuid().ToString("N"), StoreId = storeId, SignerName = Text(signerName, 120, "signerName"),
            SignerTitle = Text(signerTitle, 120, "signerTitle"), ImagePng = imagePng,
            Sha256 = Convert.ToHexString(SHA256.HashData(imagePng)).ToLowerInvariant(), WidthPx = widthPx, HeightPx = heightPx,
            SignedAtUtc = nowUtc, SignedByUserId = actor
        };
    }

    private static string Text(string value, int max, string field)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0 || text.Length > max) throw new ArgumentException($"{field} is required and must contain at most {max} characters.");
        return text;
    }
}

/// <summary>Generated occupational health and safety dossier (PDF). Kept as an official record; it does not expire.</summary>
public sealed class OhsReport
{
    public string Id { get; private set; } = null!;
    public string StoreId { get; private set; } = null!;
    public string RequestedByUserId { get; private set; } = null!;
    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public byte[] Content { get; private set; } = [];
    public long SizeBytes { get; private set; }
    public DateTime FromUtc { get; private set; }
    public DateTime ToUtc { get; private set; }
    public DateTime GeneratedAtUtc { get; private set; }
    public double ComplianceScore { get; private set; }
    public string? SignatureId { get; private set; }
    private OhsReport() { }

    public static OhsReport Create(string storeId, string requestedBy, string fileName, string contentType, byte[] content, DateTime fromUtc,
        DateTime toUtc, double score, string? signatureId, DateTime nowUtc)
    {
        if (content.Length == 0) throw new ArgumentException("Report content is empty.");
        return new()
        {
            Id = "ohsreport-" + Guid.NewGuid().ToString("N"), StoreId = storeId, RequestedByUserId = requestedBy, FileName = fileName,
            ContentType = contentType, Content = content, SizeBytes = content.LongLength, FromUtc = fromUtc, ToUtc = toUtc,
            GeneratedAtUtc = nowUtc, ComplianceScore = score, SignatureId = signatureId
        };
    }

    /// <summary>Rebuilds report metadata read without its file content (listings).</summary>
    public static OhsReport Restore(string id, string storeId, string requestedBy, string fileName, string contentType, long sizeBytes,
        DateTime fromUtc, DateTime toUtc, DateTime generatedAtUtc, double score, string? signatureId) => new()
    {
        Id = id, StoreId = storeId, RequestedByUserId = requestedBy, FileName = fileName, ContentType = contentType, SizeBytes = sizeBytes,
        FromUtc = fromUtc, ToUtc = toUtc, GeneratedAtUtc = generatedAtUtc, ComplianceScore = score, SignatureId = signatureId
    };
}
