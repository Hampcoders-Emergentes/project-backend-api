using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
namespace Hampcoders.Electrolink.API.AlertManagement.Domain;

public sealed record AlertScope(UserId Actor, string StoreId, string AreaId, string EquipmentId);
public sealed record StoreAlertScope(UserId Actor, string StoreId, string? EquipmentId = null);
/// <summary>Alert plus the names people recognise, for messages sent outside the app.</summary>
public sealed record AlertDescription(Alert Alert, string StoreName, string AreaName, string EquipmentName);
/// <summary>Alert of a store with the equipment it concerns (unscoped history for trusted internal callers).</summary>
public sealed record StoreAlertRecord(Alert Alert, string AreaName, string EquipmentName);

public interface IAlertRepository
{
    Task<Alert?> BySource(string bindingId, string readingId, CancellationToken ct);
    void Add(Alert alert);
    Task<Alert?> Find(AlertScope scope, string id, CancellationToken ct);
    Task<List<Alert>> List(AlertScope scope, string? status, string? severity, int skip, int take, CancellationToken ct, DateTime? fromUtc = null, DateTime? toUtc = null);
    Task<List<Alert>> ListStore(StoreAlertScope scope, string? status, string? severity, int skip, int take, CancellationToken ct, DateTime? fromUtc = null, DateTime? toUtc = null);
    /// <summary>Active (OPEN or ASSIGNED) alerts per equipment of the store.</summary>
    Task<Dictionary<string, (int Critical, int NonCritical)>> OpenCountsByEquipment(UserId actor, string storeId, CancellationToken ct);
    /// <summary>Store that contains the equipment (alert routing key), or null.</summary>
    Task<string?> StoreIdOfEquipment(string equipmentId, CancellationToken ct);
    Task Acknowledge(AlertScope scope, string id, DateTime now, CancellationToken ct);
    /// <summary>Active alerts of the store, critical first, newest first.</summary>
    Task<List<Alert>> ListOpen(StoreAlertScope scope, int take, CancellationToken ct);
    /// <summary>Unscoped lookup for trusted internal callers (machine dispatch).</summary>
    Task<Alert?> FindById(string id, CancellationToken ct);
    Task<AlertDescription?> Describe(string id, CancellationToken ct);
    /// <summary>Tracked alert of a store the actor owns, for state transitions; null when missing or not owned.</summary>
    Task<(Alert Alert, string StoreId)?> FindForUpdate(UserId actor, string id, CancellationToken ct);
    /// <summary>Alerts of the store created in [fromUtc, toUtc), oldest first (no ownership check: internal callers authorize first).</summary>
    Task<List<StoreAlertRecord>> History(string storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct, string? severity = null);
    /// <summary>Last change sequence handed out for the store (0 when none).</summary>
    Task<long> LatestSequence(string storeId, CancellationToken ct);
    /// <summary>Alerts of the store whose latest change is in (afterSequence, upToSequence], oldest change first.</summary>
    Task<List<Alert>> ChangesSince(StoreAlertScope scope, long afterSequence, long upToSequence, int take, CancellationToken ct);
    Task Save(CancellationToken ct);
}
