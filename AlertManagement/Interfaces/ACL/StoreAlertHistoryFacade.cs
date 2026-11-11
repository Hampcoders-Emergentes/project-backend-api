using Hampcoders.Electrolink.API.AlertManagement.Domain;

namespace Hampcoders.Electrolink.API.AlertManagement.Interfaces.ACL;

public sealed record StoreAlertHistoryEntry(string AlertId, string EquipmentId, string EquipmentName, string AreaName, string AnomalyType,
    string Severity, string Status, DateTime CreatedAtUtc, DateTime? AcknowledgedAtUtc, DateTime? AssignedAtUtc, string? AssignedTechnicianName,
    DateTime? ResolvedAtUtc, string? ResolvedByUserId, string? ResolutionNotes, string? OnDutyWorkerName);

/// <summary>Public ACL: alert history of a store, oldest first. Internal callers authorize the store first.</summary>
public interface IStoreAlertHistoryFacade
{
    Task<IReadOnlyList<StoreAlertHistoryEntry>> History(string storeId, DateTime fromUtc, DateTime toUtc, bool criticalOnly, CancellationToken ct);
}

public sealed class StoreAlertHistoryFacade(IAlertRepository alerts) : IStoreAlertHistoryFacade
{
    public async Task<IReadOnlyList<StoreAlertHistoryEntry>> History(string storeId, DateTime fromUtc, DateTime toUtc, bool criticalOnly, CancellationToken ct) =>
        (await alerts.History(storeId, fromUtc, toUtc, ct, criticalOnly ? "CRITICAL" : null)).Select(r => new StoreAlertHistoryEntry(r.Alert.Id,
            r.Alert.EquipmentId, r.EquipmentName, r.AreaName, r.Alert.AnomalyType, r.Alert.Severity, r.Alert.Status, r.Alert.CreatedAtUtc,
            r.Alert.AcknowledgedAtUtc, r.Alert.AssignedAtUtc, r.Alert.AssignedTechnicianName, r.Alert.ResolvedAtUtc, r.Alert.ResolvedByUserId,
            r.Alert.ResolutionNotes, r.Alert.OnDutyWorkerName)).ToList();
}
