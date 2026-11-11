using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;
namespace Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;

public sealed record PowerSampleRow(string EquipmentId, DateTime CapturedAtUtc, float Voltage, float Current, float PowerFactor);

public interface IEquipmentTelemetryRepository
{
    Task<EquipmentDeviceBinding?> ByEquipment(string equipmentId, CancellationToken ct);
    Task<EquipmentDeviceBinding?> ByDevice(string deviceId, CancellationToken ct);
    /// <summary>Persisted thresholds of the equipment (the single source for anomaly evaluation).</summary>
    Task<ThresholdConfig> ThresholdsOf(string equipmentId, CancellationToken ct);
    Task<bool> HasOtherIdentity(string id, CancellationToken ct);
    Task<EquipmentDeviceBinding?> ByEdgeId(string edgeId, CancellationToken ct);
    Task<EquipmentMeasurement?> FindReading(string bindingId, string readingId, CancellationToken ct);
    Task<List<EquipmentMeasurement>> Recent(string bindingId, CancellationToken ct);
    Task<List<EquipmentMeasurement>> History(Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects.UserId actor,
        string storeId, string areaId, string equipmentId, DateTime fromUtc, DateTime toUtc, int skip, int take, CancellationToken ct);
    /// <summary>All stored samples of a store's equipment in [fromUtc, toUtc), by equipment then capture time.</summary>
    Task<List<PowerSampleRow>> PowerSamples(Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects.UserId actor, string storeId,
        DateTime fromUtc, DateTime toUtc, CancellationToken ct);
    void Add(EquipmentDeviceBinding binding);
    void Add(EquipmentMeasurement reading);
    Task Save(CancellationToken ct);
}
