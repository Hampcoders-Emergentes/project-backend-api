using Hampcoders.Electrolink.API.Shared.Application.Time;
namespace Hampcoders.Electrolink.API.StoreManagement.Application;

// Explicit offsets only; bounds use PostgreSQL's microsecond precision without rounding across the interval.
public sealed record MeasurementHistoryQuery
{
    public DateTime FromUtc { get; }
    public DateTime ToUtc { get; }
    public int Page { get; }
    public int PageSize { get; }
    public int Offset { get; }
    private MeasurementHistoryQuery(DateTime from, DateTime to, int page, int size)
    { FromUtc = from; ToUtc = to; Page = page; PageSize = size; Offset = (page - 1) * size; }
    public static MeasurementHistoryQuery Parse(string? from, string? to, int page, int pageSize)
    {
        var interval = ExplicitUtcInterval.Parse(from, to);
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new ArgumentException("page must be positive; pageSize must be between 1 and 100; offset must fit Int32.");
        return new(interval.FromUtc, interval.ToUtc, page, pageSize);
    }

}

public sealed record MeasurementHistoryResource(string ReadingId, DateTime CapturedAtUtc, DateTime ReceivedAtUtc,
    string TimestampBasis, float Voltage, float Current, float PowerFactor, float Frequency, string Source,
    string? DetectedAnomalyType, string? DetectedSeverity);
