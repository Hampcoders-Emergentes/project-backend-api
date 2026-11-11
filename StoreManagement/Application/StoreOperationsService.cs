using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;

namespace Hampcoders.Electrolink.API.StoreManagement.Application;

public sealed record GetEquipmentRiskLevelQuery(UserId Actor, string StoreId, string AreaId, string EquipmentId);
public sealed record GetStoreElectricalGridStatusQuery(UserId Actor, string StoreId);
public sealed record GetSensorsConnectivityQuery(UserId Actor, string StoreId);

public sealed record ElectricalReadingResource(DateTime CapturedAtUtc, float Voltage, float Current, float PowerFactor,
    float Frequency, double ActivePowerKw, float? LeakageCurrentAmps, string? DetectedAnomalyType, string? DetectedSeverity,
    float? RelativeHumidityPercent = null, bool? PanelSurfaceMoistureDetected = null);

/// <param name="OperationalState">UNMONITORED, UNKNOWN, STALE, NORMAL or ANOMALY (same vocabulary as the dashboard).</param>
/// <param name="RiskLevel">SAFE, WARNING, CRITICAL_RISK, DISCONNECTED or UNKNOWN.</param>
public sealed record EquipmentRiskResource(string EquipmentId, string Name, string AreaId, string AreaName, string RiskLevel,
    string OperationalState, IReadOnlyList<string> Reasons, int OpenCriticalAlerts, int OpenNonCriticalAlerts,
    ElectricalReadingResource? LastReading, DateTime EvaluatedAtUtc);

public sealed record GridEquipmentResource(string EquipmentId, string Name, string AreaId, string AreaName, string RiskLevel,
    string OperationalState, ElectricalReadingResource? LastReading);

/// <param name="Status">NORMAL, WARNING, CRITICAL or NO_DATA, derived from the worst equipment risk.</param>
/// <param name="TotalActivePowerKw">Sum over equipment reporting fresh data.</param>
public sealed record StoreElectricalGridStatusResource(string StoreId, DateTime GeneratedAtUtc, string Status,
    int TotalEquipment, int ReportingEquipment, double TotalActivePowerKw, double TotalCurrentAmps,
    double? AverageVoltage, double? MinVoltage, double? MaxVoltage, double? AveragePowerFactor, double? AverageFrequency,
    double? MaxLeakageCurrentAmps, IReadOnlyDictionary<string, int> RiskLevelCounts, IReadOnlyList<GridEquipmentResource> Equipment);

/// <param name="Status">CONNECTED or DISCONNECTED (no telemetry received within the equipment's disconnection threshold).</param>
/// <param name="LastTelemetryReceivedAtUtc">Heartbeat: last time any reading from the sensor reached the platform.</param>
public sealed record SensorConnectivityResource(string SensorId, string SerialNumber, string EdgeDeviceId, string EquipmentId,
    string EquipmentName, string AreaId, string AreaName, string Status, DateTime? LastTelemetryReceivedAtUtc,
    DateTime? LastReadingCapturedAtUtc, long? SecondsSinceLastTelemetry, int DisconnectionThresholdMin, DateTime LinkedAtUtc);

public sealed record StoreSensorsConnectivityResource(string StoreId, DateTime GeneratedAtUtc, int TotalSensors, int Connected,
    int Disconnected, DateTime? LastEdgeSyncAtUtc, IReadOnlyList<SensorConnectivityResource> Sensors);

/// <summary>Live operational views of a store: per-equipment risk, electrical grid status and sensor supervision.</summary>
public sealed class StoreOperationsService(StoreHierarchyService hierarchy, IStoreDashboardReadRepository readRepository,
    IStoreAlertSummaryPort alertSummary, IStoreHierarchyRepository stores)
{
    public async Task<EquipmentRiskResource> Handle(GetEquipmentRiskLevelQuery query, CancellationToken ct)
    {
        await hierarchy.GetEquipment(query.Actor, query.StoreId, query.AreaId, query.EquipmentId, ct);
        var row = (await readRepository.LiveEquipment(query.Actor, query.StoreId, ct)).SingleOrDefault(e => e.EquipmentId == query.EquipmentId)
                  ?? throw new HierarchyNotFoundException();
        var alerts = await alertSummary.OpenAlertCountsByEquipment(query.Actor, query.StoreId, ct);
        alerts.TryGetValue(row.EquipmentId, out var counts);
        var now = DateTime.UtcNow;
        var risk = Assess(row, counts, now);
        return new(row.EquipmentId, row.Name, row.AreaId, row.AreaName, risk.Level, State(row, now), risk.Reasons,
            counts?.Critical ?? 0, counts?.NonCritical ?? 0, Reading(row.Last), now);
    }

    public async Task<StoreElectricalGridStatusResource> Handle(GetStoreElectricalGridStatusQuery query, CancellationToken ct)
    {
        await hierarchy.GetStore(query.Actor, query.StoreId, ct);
        var rows = await readRepository.LiveEquipment(query.Actor, query.StoreId, ct);
        var alerts = await alertSummary.OpenAlertCountsByEquipment(query.Actor, query.StoreId, ct);
        var now = DateTime.UtcNow;
        var equipment = rows.Select(r =>
        {
            alerts.TryGetValue(r.EquipmentId, out var counts);
            return (Row: r, Risk: Assess(r, counts, now).Level);
        }).ToList();
        // Aggregates only use fresh data: a disconnected sensor's last value is not the current state of the grid.
        var fresh = equipment.Where(e => e.Row.Last != null && e.Risk != EquipmentRiskLevel.Disconnected).Select(e => e.Row.Last!).ToList();
        var counts = new[] { EquipmentRiskLevel.Safe, EquipmentRiskLevel.Warning, EquipmentRiskLevel.CriticalRisk,
                EquipmentRiskLevel.Disconnected, EquipmentRiskLevel.Unknown }
            .ToDictionary(level => level, level => equipment.Count(e => e.Risk == level));
        var status = equipment.Count == 0 || equipment.All(e => e.Risk == EquipmentRiskLevel.Unknown) ? "NO_DATA"
            : counts[EquipmentRiskLevel.CriticalRisk] > 0 ? "CRITICAL"
            : counts[EquipmentRiskLevel.Warning] + counts[EquipmentRiskLevel.Disconnected] > 0 ? "WARNING" : "NORMAL";
        return new(query.StoreId, now, status, equipment.Count, fresh.Count,
            Math.Round(fresh.Sum(ActiveKw), 4), Math.Round(fresh.Sum(s => (double)s.Current), 3),
            Average(fresh, s => s.Voltage), fresh.Count == 0 ? null : fresh.Min(s => (double)s.Voltage),
            fresh.Count == 0 ? null : fresh.Max(s => (double)s.Voltage), Average(fresh, s => s.PowerFactor),
            Average(fresh, s => s.Frequency),
            fresh.Any(s => s.LeakageCurrentAmps != null) ? fresh.Max(s => (double?)s.LeakageCurrentAmps) : null,
            counts,
            equipment.OrderByDescending(e => EquipmentRiskLevel.Rank(e.Risk)).ThenBy(e => e.Row.AreaName, StringComparer.Ordinal)
                .ThenBy(e => e.Row.Name, StringComparer.Ordinal)
                .Select(e => new GridEquipmentResource(e.Row.EquipmentId, e.Row.Name, e.Row.AreaId, e.Row.AreaName, e.Risk,
                    State(e.Row, now), Reading(e.Row.Last))).ToList());
    }

    public async Task<StoreSensorsConnectivityResource> Handle(GetSensorsConnectivityQuery query, CancellationToken ct)
    {
        await hierarchy.GetStore(query.Actor, query.StoreId, ct);
        var store = await stores.FindStoreAsync(query.Actor, query.StoreId, ct);
        var rows = await readRepository.LiveEquipment(query.Actor, query.StoreId, ct);
        var now = DateTime.UtcNow;
        var sensors = rows.Where(r => r.Sensor != null).Select(r =>
        {
            var threshold = r.Thresholds.DisconnectionThresholdMin;
            var connected = r.LastReceivedAtUtc != null && now - r.LastReceivedAtUtc.Value <= TimeSpan.FromMinutes(threshold);
            return new SensorConnectivityResource(r.Sensor!.DeviceId, r.Sensor.SerialNumber, r.Sensor.EdgeDeviceId, r.EquipmentId,
                r.Name, r.AreaId, r.AreaName, connected ? "CONNECTED" : "DISCONNECTED", r.LastReceivedAtUtc, r.Last?.CapturedAtUtc,
                r.LastReceivedAtUtc == null ? null : (long)Math.Max(0, (now - r.LastReceivedAtUtc.Value).TotalSeconds),
                threshold, r.Sensor.LinkedAtUtc);
        }).OrderBy(s => s.Status == "CONNECTED" ? 1 : 0).ThenBy(s => s.AreaName, StringComparer.Ordinal)
            .ThenBy(s => s.EquipmentName, StringComparer.Ordinal).ToList();
        return new(query.StoreId, now, sensors.Count, sensors.Count(s => s.Status == "CONNECTED"),
            sensors.Count(s => s.Status == "DISCONNECTED"), store?.LastEdgeSyncAtUtc, sensors);
    }

    /// <summary>Active alerts of the whole store (the caller has authorized the store).</summary>
    public async Task<EquipmentAlertCounts> ActiveAlertTotals(UserId actor, string storeId, CancellationToken ct)
    {
        var counts = await alertSummary.OpenAlertCountsByEquipment(actor, storeId, ct);
        return new(counts.Values.Sum(c => c.Critical), counts.Values.Sum(c => c.NonCritical));
    }

    internal static EquipmentRiskAssessment Assess(EquipmentLiveRow row, EquipmentAlertCounts? counts, DateTime now) =>
        EquipmentRiskPolicy.Evaluate(new EquipmentRiskInput(row.Sensor != null, row.Thresholds, row.Last,
            counts?.Critical ?? 0, counts?.NonCritical ?? 0), now);

    internal static string State(EquipmentLiveRow row, DateTime now) =>
        row.Sensor == null ? "UNMONITORED"
        : row.Last == null ? "UNKNOWN"
        : now - row.Last.CapturedAtUtc > TimeSpan.FromMinutes(row.Thresholds.DisconnectionThresholdMin) ? "STALE"
        : row.Last.AnomalyType == null ? "NORMAL" : "ANOMALY";

    // Same formula as the Processing reading model: kW = V * A / 1000 * power factor.
    private static double ActiveKw(EquipmentElectricalSnapshot s) => (double)s.Voltage * s.Current / 1000.0 * s.PowerFactor;

    private static double? Average(IReadOnlyCollection<EquipmentElectricalSnapshot> samples, Func<EquipmentElectricalSnapshot, float> value) =>
        samples.Count == 0 ? null : Math.Round(samples.Average(s => (double)value(s)), 3);

    internal static ElectricalReadingResource? Reading(EquipmentElectricalSnapshot? s) => s == null ? null
        : new(s.CapturedAtUtc, s.Voltage, s.Current, s.PowerFactor, s.Frequency, Math.Round(ActiveKw(s), 4), s.LeakageCurrentAmps,
            s.AnomalyType, s.AnomalySeverity, s.RelativeHumidityPercent, s.PanelSurfaceMoistureDetected);
}
