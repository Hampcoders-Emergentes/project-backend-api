using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
namespace Hampcoders.Electrolink.API.AlertManagement.Domain;

public sealed record AlertScope(UserId Actor, string StoreId, string AreaId, string EquipmentId);
public sealed record StoreAlertScope(UserId Actor, string StoreId, string? EquipmentId = null);
public interface IAlertRepository
{
    Task<Alert?> BySource(string bindingId, string readingId, CancellationToken ct);
    void Add(Alert alert);
    Task<Alert?> Find(AlertScope scope, string id, CancellationToken ct);
    Task<List<Alert>> List(AlertScope scope, string? status, string? severity, int skip, int take, CancellationToken ct, DateTime? fromUtc = null, DateTime? toUtc = null);
    Task<List<Alert>> ListStore(StoreAlertScope scope, string? status, string? severity, int skip, int take, CancellationToken ct, DateTime? fromUtc = null, DateTime? toUtc = null);
    Task Acknowledge(AlertScope scope, string id, DateTime now, CancellationToken ct);
}
