using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using UserId = Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects.UserId;
namespace Hampcoders.Electrolink.API.AlertManagement.Infrastructure;

// Shared transitional transaction boundary; no independent SaveChanges during materialization.
public sealed class AlertRepository(StoreManagementDbContext context) : IAlertRepository
{
    public Task<Alert?> BySource(string bindingId, string readingId, CancellationToken ct)
    {
        var staged = context.Alerts.Local.SingleOrDefault(a => a.BindingId == bindingId && a.ReadingId == readingId);
        return staged != null ? Task.FromResult<Alert?>(staged) : context.Alerts.AsNoTracking().SingleOrDefaultAsync(a => a.BindingId == bindingId && a.ReadingId == readingId, ct);
    }
    public void Add(Alert alert) => context.Alerts.Add(alert);
    private IQueryable<Alert> Scoped(AlertScope scope) => Scoped(scope.Actor, scope.StoreId, scope.AreaId, scope.EquipmentId);
    // Scoped by the equipment's store ownership, so alerts reported by hand (no sensor binding) are included.
    private IQueryable<Alert> Scoped(UserId actor, string storeId, string? areaId, string? equipmentId) =>
        from alert in context.Alerts.AsNoTracking()
        join equipment in context.Equipment on alert.EquipmentId equals equipment.Id
        join area in context.Areas on equipment.AreaId equals area.Id
        join store in context.Stores on area.StoreId equals store.Id
        where (equipmentId == null || equipment.Id == equipmentId) && (areaId == null || area.Id == areaId) && store.Id == storeId
            && store.OwnerUserId == actor && alert.OwnerUserId == actor.Value
        select alert;
    public Task<Alert?> Find(AlertScope scope, string id, CancellationToken ct) => Scoped(scope).SingleOrDefaultAsync(a => a.Id == id, ct);
    public Task<List<Alert>> List(AlertScope scope, string? status, string? severity, int skip, int take, CancellationToken ct,
        DateTime? fromUtc = null, DateTime? toUtc = null) => List(Scoped(scope), status, severity, skip, take, ct, fromUtc, toUtc);
    public Task<List<Alert>> ListStore(StoreAlertScope scope, string? status, string? severity, int skip, int take, CancellationToken ct,
        DateTime? fromUtc = null, DateTime? toUtc = null) =>
        List(Scoped(scope.Actor, scope.StoreId, null, scope.EquipmentId), status, severity, skip, take, ct, fromUtc, toUtc);
    private static Task<List<Alert>> List(IQueryable<Alert> scoped, string? status, string? severity, int skip, int take, CancellationToken ct, DateTime? fromUtc, DateTime? toUtc)
    {
        var query = scoped.Where(a => (status == null || a.Status == status) && (severity == null || a.Severity == severity));
        if (fromUtc.HasValue) query = query.Where(a => a.CreatedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(a => a.CreatedAtUtc < toUtc.Value);
        return query.OrderByDescending(a => a.CreatedAtUtc).ThenBy(a => a.Id).Skip(skip).Take(take).ToListAsync(ct);
    }
    public async Task<Dictionary<string, (int Critical, int NonCritical)>> OpenCountsByEquipment(UserId actor, string storeId, CancellationToken ct)
    {
        var active = AlertStatus.Active.ToArray();
        var rows = await Scoped(actor, storeId, null, null).Where(a => active.Contains(a.Status))
            .GroupBy(a => new { a.EquipmentId, a.Severity })
            .Select(g => new { g.Key.EquipmentId, g.Key.Severity, Count = g.Count() }).ToListAsync(ct);
        return rows.GroupBy(r => r.EquipmentId).ToDictionary(g => g.Key,
            g => (g.Where(r => r.Severity == "CRITICAL").Sum(r => r.Count), g.Where(r => r.Severity != "CRITICAL").Sum(r => r.Count)));
    }
    public Task<string?> StoreIdOfEquipment(string equipmentId, CancellationToken ct) =>
        (from e in context.Equipment.AsNoTracking() join a in context.Areas.AsNoTracking() on e.AreaId equals a.Id
         where e.Id == equipmentId select (string?)a.StoreId).SingleOrDefaultAsync(ct);
    public Task<List<Alert>> ListOpen(StoreAlertScope scope, int take, CancellationToken ct)
    {
        var active = AlertStatus.Active.ToArray();
        return Scoped(scope.Actor, scope.StoreId, null, scope.EquipmentId).Where(a => active.Contains(a.Status))
            .OrderBy(a => a.Severity == "CRITICAL" ? 0 : 1).ThenByDescending(a => a.CreatedAtUtc).ThenBy(a => a.Id)
            .Take(take).ToListAsync(ct);
    }
    public Task<Alert?> FindById(string id, CancellationToken ct) => context.Alerts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);
    public Task<AlertDescription?> Describe(string id, CancellationToken ct) =>
        (from alert in context.Alerts.AsNoTracking()
         join equipment in context.Equipment.AsNoTracking() on alert.EquipmentId equals equipment.Id
         join area in context.Areas.AsNoTracking() on equipment.AreaId equals area.Id
         join store in context.Stores.AsNoTracking() on area.StoreId equals store.Id
         where alert.Id == id
         select new AlertDescription(alert, store.Name, area.Name, equipment.Name)).SingleOrDefaultAsync(ct);
    public async Task Acknowledge(AlertScope scope, string id, DateTime now, CancellationToken ct)
    {
        now = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc);
        // Single conditional SQL statement: first acknowledgement wins, including concurrent requests.
        await Scoped(scope).Where(a => a.Id == id && a.Status == AlertStatus.Open).ExecuteUpdateAsync(setters => setters
            .SetProperty(a => a.Status, AlertStatus.Acknowledged).SetProperty(a => a.AcknowledgedByUserId, scope.Actor.Value)
            .SetProperty(a => a.AcknowledgedAtUtc, (DateTime?)now), ct);
    }
    public async Task<(Alert Alert, string StoreId)?> FindForUpdate(UserId actor, string id, CancellationToken ct)
    {
        var row = await (from alert in context.Alerts
                         join equipment in context.Equipment on alert.EquipmentId equals equipment.Id
                         join area in context.Areas on equipment.AreaId equals area.Id
                         join store in context.Stores on area.StoreId equals store.Id
                         where alert.Id == id && store.OwnerUserId == actor && alert.OwnerUserId == actor.Value
                         select new { alert, StoreId = store.Id }).SingleOrDefaultAsync(ct);
        return row == null ? null : (row.alert, row.StoreId);
    }
    public Task<List<StoreAlertRecord>> History(string storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct, string? severity = null) =>
        (from alert in context.Alerts.AsNoTracking()
         join equipment in context.Equipment.AsNoTracking() on alert.EquipmentId equals equipment.Id
         join area in context.Areas.AsNoTracking() on equipment.AreaId equals area.Id
         where area.StoreId == storeId && alert.CreatedAtUtc >= fromUtc && alert.CreatedAtUtc < toUtc
             && (severity == null || alert.Severity == severity)
         orderby alert.CreatedAtUtc, alert.Id
         select new StoreAlertRecord(alert, area.Name, equipment.Name)).ToListAsync(ct);
    public Task Save(CancellationToken ct) => context.SaveChangesAsync(ct);
}
