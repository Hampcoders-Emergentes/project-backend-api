using Hampcoders.Electrolink.API.AlertManagement.Domain;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

// Synthetic persistence only; transaction guarantees are verified separately against PostgreSQL.
internal sealed class MemoryAlertRepository : IAlertRepository
{
    internal Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories.IStoreHierarchyRepository? Hierarchy { get; set; }
    internal List<Alert> Rows { get; } = new();
    public Task<Alert?> BySource(string bindingId, string readingId, CancellationToken ct) => Task.FromResult(Rows.SingleOrDefault(a => a.BindingId == bindingId && a.ReadingId == readingId));
    public void Add(Alert a) => Rows.Add(a);
    private IEnumerable<Alert> Scoped(AlertScope scope) => Rows.Where(a => a.OwnerUserId == scope.Actor.Value && a.EquipmentId == scope.EquipmentId);
    public Task<Alert?> Find(AlertScope scope, string id, CancellationToken ct) => Task.FromResult(Scoped(scope).SingleOrDefault(a => a.Id == id));
    public Task<List<Alert>> List(AlertScope scope, string? status, string? severity, int skip, int take, CancellationToken ct, DateTime? fromUtc = null, DateTime? toUtc = null) => Task.FromResult(
        Scoped(scope).Where(a => (status == null || a.Status == status) && (severity == null || a.Severity == severity)
            && (fromUtc == null || a.CreatedAtUtc >= fromUtc.Value) && (toUtc == null || a.CreatedAtUtc < toUtc.Value))
            .OrderByDescending(a => a.CreatedAtUtc).ThenBy(a => a.Id).Skip(skip).Take(take).ToList());
    public async Task<List<Alert>> ListStore(StoreAlertScope scope, string? status, string? severity, int skip, int take, CancellationToken ct, DateTime? fromUtc = null, DateTime? toUtc = null)
    {
        var rows = new List<Alert>();
        foreach (var a in Rows.Where(a => a.OwnerUserId == scope.Actor.Value && (scope.EquipmentId == null || a.EquipmentId == scope.EquipmentId)))
            if (await Hierarchy!.FindEquipmentInStoreAsync(scope.Actor, scope.StoreId, a.EquipmentId, ct) != null) rows.Add(a);
        return rows.Where(a => (status == null || a.Status == status) && (severity == null || a.Severity == severity)
            && (fromUtc == null || a.CreatedAtUtc >= fromUtc) && (toUtc == null || a.CreatedAtUtc < toUtc))
            .OrderByDescending(a => a.CreatedAtUtc).ThenBy(a => a.Id, StringComparer.Ordinal).Skip(skip).Take(take).ToList();
    }
    public Task Acknowledge(AlertScope scope, string id, DateTime now, CancellationToken ct)
    { Scoped(scope).Single(a => a.Id == id).Acknowledge(scope.Actor, now); return Task.CompletedTask; }
}
