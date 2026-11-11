using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;

namespace Hampcoders.Electrolink.API.StoreManagement.Application;

public sealed record EquipmentAlertCounts(int Critical, int NonCritical);

/// <summary>Outbound port to AlertManagement: open alert counters per equipment of a store (no domain types cross).</summary>
public interface IStoreAlertSummaryPort
{
    Task<IReadOnlyDictionary<string, EquipmentAlertCounts>> OpenAlertCountsByEquipment(UserId actor, string storeId, CancellationToken ct);
}

public sealed record GetStoreDashboardSummaryQuery(UserId Actor, string StoreId);

public sealed record EquipmentStatusResource(string EquipmentId, string Name, string AreaId, string AreaName, string State,
    DateTime? LastMeasurementAtUtc, int OpenCriticalAlerts, int OpenNonCriticalAlerts);

public sealed record StoreDashboardSummaryResource(string StoreId, DateTime GeneratedAtUtc, int TotalEquipment,
    int MonitoredEquipment, int OfflineEquipment, int UnmonitoredEquipment, int ActiveCriticalAlerts,
    int ActiveNonCriticalAlerts, IReadOnlyList<EquipmentStatusResource> Equipment);

/// <summary>
///     Single-call projection for the store dashboard. States: UNMONITORED (no device bound), UNKNOWN (bound, no data),
///     STALE (no data within the disconnection threshold), NORMAL, ANOMALY. UNKNOWN and STALE count as offline.
///     Device-identity verification is not repeated here; it is enforced on ingestion and in the per-equipment telemetry view.
/// </summary>
public sealed class StoreDashboardService(StoreHierarchyService hierarchy, IStoreDashboardReadRepository readRepository,
    IStoreAlertSummaryPort alertSummary)
{
    public async Task<StoreDashboardSummaryResource> Handle(GetStoreDashboardSummaryQuery query, CancellationToken ct)
    {
        await hierarchy.GetStore(query.Actor, query.StoreId, ct);
        var rows = await readRepository.EquipmentStatus(query.Actor, query.StoreId, ct);
        var alerts = await alertSummary.OpenAlertCountsByEquipment(query.Actor, query.StoreId, ct);
        var now = DateTime.UtcNow;
        var equipment = rows.Select(r =>
        {
            alerts.TryGetValue(r.EquipmentId, out var counts);
            var state = !r.IsBound ? "UNMONITORED"
                : r.LastCapturedAtUtc == null ? "UNKNOWN"
                : now - r.LastCapturedAtUtc > TimeSpan.FromMinutes(r.DisconnectionThresholdMin) ? "STALE"
                : r.LastAnomalyType == null ? "NORMAL" : "ANOMALY";
            return new EquipmentStatusResource(r.EquipmentId, r.Name, r.AreaId, r.AreaName, state, r.LastCapturedAtUtc,
                counts?.Critical ?? 0, counts?.NonCritical ?? 0);
        }).ToList();
        return new(query.StoreId, now, equipment.Count, equipment.Count(e => e.State != "UNMONITORED"),
            equipment.Count(e => e.State is "UNKNOWN" or "STALE"), equipment.Count(e => e.State == "UNMONITORED"),
            equipment.Sum(e => e.OpenCriticalAlerts), equipment.Sum(e => e.OpenNonCriticalAlerts), equipment);
    }
}
