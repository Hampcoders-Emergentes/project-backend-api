using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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
    private IQueryable<Alert> Scoped(Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects.UserId actor,
        string storeId, string? areaId, string? equipmentId) =>
        from alert in context.Alerts.AsNoTracking()
        join binding in context.DeviceBindings on alert.BindingId equals binding.Id
        join equipment in context.Equipment on binding.EquipmentId equals equipment.Id
        join area in context.Areas on equipment.AreaId equals area.Id
        join store in context.Stores on area.StoreId equals store.Id
        where alert.EquipmentId == equipment.Id && (equipmentId == null || equipment.Id == equipmentId) && (areaId == null || area.Id == areaId) && store.Id == storeId
            && store.OwnerUserId == actor && binding.OwnerUserId == actor && alert.OwnerUserId == actor.Value
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
    public async Task Acknowledge(AlertScope scope, string id, DateTime now, CancellationToken ct)
    {
        now = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc);
        // Single conditional SQL statement: first acknowledgement wins, including concurrent requests.
        await Scoped(scope).Where(a => a.Id == id && a.Status == "OPEN").ExecuteUpdateAsync(setters => setters
            .SetProperty(a => a.Status, "ACKNOWLEDGED").SetProperty(a => a.AcknowledgedByUserId, scope.Actor.Value)
            .SetProperty(a => a.AcknowledgedAtUtc, (DateTime?)now), ct);
    }
}
