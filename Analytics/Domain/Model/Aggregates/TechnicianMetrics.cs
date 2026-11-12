using Hampcoders.Electrolink.API.Analytics.Domain.Model.Events;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Analytics.Domain.Model.Aggregates;

public class TechnicianMetrics : BaseAggregateRoot
{
    public TechnicianMetricsId MetricsId { get; private set; }
    public TechnicianId TechnicianId { get; private set; }
    public DateRange Period { get; private set; }
    public int CompletedServicesCount { get; private set; }
    public int IoTServicesCount { get; private set; }
    public decimal AverageRating { get; private set; }
    public Money TotalRevenue { get; private set; }
    public TimeSpan AverageResponseTime { get; private set; }
    public decimal ClientRetentionRate { get; private set; }
    public DateTime LastCalculatedAt { get; private set; }

    private int _totalRatingCount;
    private decimal _totalRatingSum;
    private long _totalResponseTimeTicks;
    private int _returningClientCount;
    private HashSet<string> _servedClientIds;

    private TechnicianMetrics() { }

    public static TechnicianMetrics InitializeForPeriod(TechnicianId technicianId, DateRange period)
    {
        return new TechnicianMetrics
        {
            MetricsId = TechnicianMetricsId.New(),
            TechnicianId = technicianId ?? throw new ArgumentNullException(nameof(technicianId)),
            Period = period,
            CompletedServicesCount = 0,
            IoTServicesCount = 0,
            AverageRating = 0,
            TotalRevenue = Money.Zero("PEN"),
            AverageResponseTime = TimeSpan.Zero,
            ClientRetentionRate = 0,
            LastCalculatedAt = DateTime.UtcNow,
            _totalRatingCount = 0,
            _totalRatingSum = 0,
            _totalResponseTimeTicks = 0,
            _returningClientCount = 0,
            _servedClientIds = new HashSet<string>()
        };
    }

    public void ApplyServiceCompleted(
        string clientId,
        Money serviceRevenue,
        TimeSpan responseTime,
        bool requiresIoTCertification)
    {
        CompletedServicesCount++;

        if (requiresIoTCertification)
            IoTServicesCount++;

        TotalRevenue = TotalRevenue.Add(serviceRevenue);

        _totalResponseTimeTicks += responseTime.Ticks;
        AverageResponseTime = new TimeSpan(_totalResponseTimeTicks / CompletedServicesCount);

        if (_servedClientIds.Contains(clientId))
            _returningClientCount++;
        _servedClientIds.Add(clientId);
        ClientRetentionRate = _servedClientIds.Count > 0
            ? (decimal)_returningClientCount / _servedClientIds.Count
            : 0;

        LastCalculatedAt = DateTime.UtcNow;

        RaiseDomainEvent(new TechnicianMetricsUpdated(
            MetricsId.Value,
            TechnicianId.Value,
            CompletedServicesCount,
            TotalRevenue.Amount,
            DateTime.UtcNow));
    }

    public void ApplyRatingReceived(decimal rating)
    {
        if (rating < 1 || rating > 5)
            throw new ArgumentException("Rating must be between 1 and 5.");

        _totalRatingCount++;
        _totalRatingSum += rating;
        AverageRating = _totalRatingSum / _totalRatingCount;
        LastCalculatedAt = DateTime.UtcNow;
    }
}
