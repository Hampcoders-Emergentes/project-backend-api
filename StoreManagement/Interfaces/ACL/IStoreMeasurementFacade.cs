using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.StoreManagement.Interfaces.ACL;

public sealed record EquipmentPowerSample(string EquipmentId, DateTime CapturedAtUtc, double ActivePowerKw);
public sealed record StorePowerData(IReadOnlyDictionary<string, string> EquipmentNames, IReadOnlyList<EquipmentPowerSample> Samples);

public sealed record EquipmentElectricalLimits(string EquipmentId, string Name, double NominalVoltage, double MaxCurrentAmps, double MinPowerFactor);
public sealed record EquipmentElectricalSample(string EquipmentId, DateTime CapturedAtUtc, double Voltage, double Current, double PowerFactor);
public sealed record StoreElectricalData(IReadOnlyList<EquipmentElectricalLimits> Equipment, IReadOnlyList<EquipmentElectricalSample> Samples);

/// <summary>Public ACL of StoreManagement for other contexts: power samples of a store the actor owns.</summary>
public interface IStoreMeasurementFacade
{
    /// <exception cref="Application.HierarchyNotFoundException">Store missing or not owned by the actor.</exception>
    Task<StorePowerData> PowerSamples(UserId actor, string storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);

    /// <summary>Samples of one equipment, verified to belong to the area and store of the actor.</summary>
    /// <exception cref="Application.HierarchyNotFoundException">Equipment missing or not owned by the actor.</exception>
    Task<StorePowerData> EquipmentPowerSamples(UserId actor, string storeId, string areaId, string equipmentId,
        DateTime fromUtc, DateTime toUtc, CancellationToken ct);

    /// <summary>Voltage, current and power factor samples of every equipment of the store, with each equipment's limits.</summary>
    /// <exception cref="Application.HierarchyNotFoundException">Store missing or not owned by the actor.</exception>
    Task<StoreElectricalData> ElectricalSamples(UserId actor, string storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);
}
