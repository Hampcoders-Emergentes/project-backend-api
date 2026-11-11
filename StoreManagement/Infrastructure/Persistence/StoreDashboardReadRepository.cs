using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
namespace Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;

public sealed class StoreDashboardReadRepository(StoreManagementDbContext context) : IStoreDashboardReadRepository
{
    public async Task<IReadOnlyList<EquipmentStatusRow>> EquipmentStatus(UserId actor, string storeId, CancellationToken ct)
    {
        var equipment = await (from e in context.Equipment.AsNoTracking()
                               join a in context.Areas.AsNoTracking() on e.AreaId equals a.Id
                               join s in context.Stores.AsNoTracking() on a.StoreId equals s.Id
                               where s.Id == storeId && s.OwnerUserId == actor
                               from b in context.DeviceBindings.AsNoTracking().Where(b => b.EquipmentId == e.Id).DefaultIfEmpty()
                               orderby a.Name, e.Name, e.Id
                               select new { e.Id, e.Name, AreaId = a.Id, AreaName = a.Name, BindingId = b == null ? null : b.Id,
                                   e.Thresholds.DisconnectionThresholdMin }).ToListAsync(ct);
        var bindingIds = equipment.Where(e => e.BindingId != null).Select(e => e.BindingId!).ToList();
        // Latest measurement per binding, in one query (ties on capture time resolved by reading id).
        var latest = bindingIds.Count == 0 ? [] : await context.Measurements.AsNoTracking()
            .Where(m => bindingIds.Contains(m.BindingId)
                && m.CapturedAtUtc == context.Measurements.Where(x => x.BindingId == m.BindingId).Max(x => x.CapturedAtUtc))
            .Select(m => new { m.BindingId, m.ReadingId, m.CapturedAtUtc, m.AnomalyType }).ToListAsync(ct);
        var byBinding = latest.GroupBy(m => m.BindingId).ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.ReadingId, StringComparer.Ordinal).First());
        return equipment.Select(e =>
        {
            var last = e.BindingId != null && byBinding.TryGetValue(e.BindingId, out var m) ? m : null;
            return new EquipmentStatusRow(e.Id, e.Name, e.AreaId, e.AreaName, e.BindingId != null, e.DisconnectionThresholdMin,
                last?.CapturedAtUtc, last?.AnomalyType);
        }).ToList();
    }
}
