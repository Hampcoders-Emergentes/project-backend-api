using Hampcoders.Electrolink.API.Processing.Domain.Model.Aggregates;

namespace Hampcoders.Electrolink.API.Processing.Domain.Services;

// Human access to monitoring data: the owner of the stream, or a technician with an active service at its property.
public interface IDeviceAccessService
{
    Task<bool> CanAccessDeviceAsync(string userId, string deviceId);
    Task<bool> CanAccessAnomalyAsync(string userId, string anomalyId);
    Task<IReadOnlyList<AnomalyRecord>> FilterVisibleAnomaliesAsync(string userId, string propertyId, IEnumerable<AnomalyRecord> anomalies);
}
