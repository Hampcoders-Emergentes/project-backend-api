using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
namespace Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;

public interface IEquipmentTelemetryRepository
{
    Task<EquipmentDeviceBinding?> ByEquipment(string equipmentId, CancellationToken ct);
    Task<EquipmentDeviceBinding?> ByDevice(string deviceId, CancellationToken ct);
    Task<bool> HasOtherIdentity(string id, CancellationToken ct);
    Task<EquipmentDeviceBinding?> ByEdgeId(string edgeId, CancellationToken ct);
    Task<EquipmentMeasurement?> FindReading(string bindingId, string readingId, CancellationToken ct);
    Task<List<EquipmentMeasurement>> Recent(string bindingId, CancellationToken ct);
    Task<List<EquipmentMeasurement>> History(Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects.UserId actor,
        string storeId, string areaId, string equipmentId, DateTime fromUtc, DateTime toUtc, int skip, int take, CancellationToken ct);
    void Add(EquipmentDeviceBinding binding);
    void Add(EquipmentMeasurement reading);
    Task Save(CancellationToken ct);
}
