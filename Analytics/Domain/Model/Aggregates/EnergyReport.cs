namespace Hampcoders.Electrolink.API.Analytics.Domain.Model.Aggregates;

/// <summary>A generated energy efficiency report file, downloadable by the store owner until it expires.</summary>
public sealed class EnergyReport
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public string Id { get; private set; } = null!;
    public string StoreId { get; private set; } = null!;
    public string RequestedByUserId { get; private set; } = null!;
    public string Format { get; private set; } = null!;
    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public byte[] Content { get; private set; } = [];
    public long SizeBytes { get; private set; }
    public DateTime FromUtc { get; private set; }
    public DateTime ToUtc { get; private set; }
    public DateTime GeneratedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    private EnergyReport() { }

    public static EnergyReport Create(string storeId, string requestedByUserId, string format, string fileName, string contentType,
        byte[] content, DateTime fromUtc, DateTime toUtc, DateTime nowUtc)
    {
        if (content.Length == 0) throw new ArgumentException("Report content is empty.");
        return new()
        {
            Id = "report-" + Guid.NewGuid().ToString("N"), StoreId = storeId, RequestedByUserId = requestedByUserId, Format = format,
            FileName = fileName, ContentType = contentType, Content = content, SizeBytes = content.LongLength, FromUtc = fromUtc,
            ToUtc = toUtc, GeneratedAtUtc = nowUtc, ExpiresAtUtc = nowUtc + Lifetime
        };
    }

    public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresAtUtc;
}
