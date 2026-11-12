using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;

namespace Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;

/// <summary>One row per equipment of a store: hierarchy data, binding presence and last measurement.</summary>
public sealed record EquipmentStatusRow(string EquipmentId, string Name, string AreaId, string AreaName, bool IsBound,
    int DisconnectionThresholdMin, DateTime? LastCapturedAtUtc, string? LastAnomalyType);

/// <summary>Sensor linked to an equipment.</summary>
public sealed record SensorBindingRow(string BindingId, string DeviceId, string SerialNumber, string EdgeDeviceId, DateTime LinkedAtUtc);

/// <summary>
///     Live view of one equipment: limits, sensor, latest measurement (by capture time) and the last time any
///     telemetry from its sensor reached the platform (by reception time, which includes replayed readings).
/// </summary>
public sealed record EquipmentLiveRow(string EquipmentId, string Name, string AreaId, string AreaName, ThresholdConfig Thresholds,
    SensorBindingRow? Sensor, EquipmentElectricalSnapshot? Last, DateTime? LastReceivedAtUtc);

public interface IStoreDashboardReadRepository
{
    /// <summary>Constant number of queries regardless of equipment count; empty if the actor does not own the store.</summary>
    Task<IReadOnlyList<EquipmentStatusRow>> EquipmentStatus(UserId actor, string storeId, CancellationToken ct);

    /// <summary>Constant number of queries regardless of equipment count; empty if the actor does not own the store.</summary>
    Task<IReadOnlyList<EquipmentLiveRow>> LiveEquipment(UserId actor, string storeId, CancellationToken ct);
}
