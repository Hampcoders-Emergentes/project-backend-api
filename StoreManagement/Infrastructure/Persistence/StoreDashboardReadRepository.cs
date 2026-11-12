using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
namespace Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;

public sealed class StoreDashboardReadRepository(StoreManagementDbContext context) : IStoreDashboardReadRepository
{
    public async Task<IReadOnlyList<EquipmentStatusRow>> EquipmentStatus(UserId actor, string storeId, CancellationToken ct) =>
        (await LiveEquipment(actor, storeId, ct)).Select(e => new EquipmentStatusRow(e.EquipmentId, e.Name, e.AreaId, e.AreaName,
            e.Sensor != null, e.Thresholds.DisconnectionThresholdMin, e.Last?.CapturedAtUtc, e.Last?.AnomalyType)).ToList();

    public async Task<IReadOnlyList<EquipmentLiveRow>> LiveEquipment(UserId actor, string storeId, CancellationToken ct)
    {
        var equipment = await (from e in context.Equipment.AsNoTracking()
                               join a in context.Areas.AsNoTracking() on e.AreaId equals a.Id
                               join s in context.Stores.AsNoTracking() on a.StoreId equals s.Id
                               where s.Id == storeId && s.OwnerUserId == actor
                               from b in context.DeviceBindings.AsNoTracking().Where(b => b.EquipmentId == e.Id).DefaultIfEmpty()
                               orderby a.Name, e.Name, e.Id
                               select new
                               {
                                   e.Id, e.Name, AreaId = a.Id, AreaName = a.Name,
                                   e.Thresholds.NominalVoltage, e.Thresholds.MaxConsumptionWatts, e.Thresholds.MaxCurrentAmps,
                                   e.Thresholds.MinPowerFactor, e.Thresholds.NominalFrequency, e.Thresholds.DisconnectionThresholdMin,
                                   e.Thresholds.NormalLimitAmps, e.Thresholds.AlertLimitAmps,
                                   BindingId = b == null ? null : b.Id, DeviceId = b == null ? null : b.DeviceId,
                                   SerialNumber = b == null ? null : b.SerialNumber, EdgeDeviceId = b == null ? null : b.EdgeDeviceId,
                                   LinkedAtUtc = b == null ? (DateTime?)null : b.LinkedAtUtc
                               }).ToListAsync(ct);
        var bindingIds = equipment.Where(e => e.BindingId != null).Select(e => e.BindingId!).ToList();
        // Latest measurement per binding, in one query (ties on capture time resolved by reading id).
        var latest = bindingIds.Count == 0 ? [] : await context.Measurements.AsNoTracking()
            .Where(m => bindingIds.Contains(m.BindingId)
                && m.CapturedAtUtc == context.Measurements.Where(x => x.BindingId == m.BindingId).Max(x => x.CapturedAtUtc))
            .ToListAsync(ct);
        var byBinding = latest.GroupBy(m => m.BindingId).ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.ReadingId, StringComparer.Ordinal).First());
        // Heartbeat: last reception per binding (replayed readings keep their capture time but are received now).
        var received = bindingIds.Count == 0 ? [] : await context.Measurements.AsNoTracking()
            .Where(m => bindingIds.Contains(m.BindingId)).GroupBy(m => m.BindingId)
            .Select(g => new { BindingId = g.Key, At = g.Max(m => m.ReceivedAtUtc) }).ToListAsync(ct);
        var receivedBy = received.ToDictionary(r => r.BindingId, r => r.At);
        return equipment.Select(e =>
        {
            var last = e.BindingId != null && byBinding.TryGetValue(e.BindingId, out var m) ? m : null;
            var thresholds = ThresholdConfig.Create(e.NominalVoltage, e.MaxConsumptionWatts, e.MaxCurrentAmps, e.MinPowerFactor,
                e.NominalFrequency, e.DisconnectionThresholdMin, e.NormalLimitAmps, e.AlertLimitAmps);
            return new EquipmentLiveRow(e.Id, e.Name, e.AreaId, e.AreaName, thresholds,
                e.BindingId == null ? null : new SensorBindingRow(e.BindingId, e.DeviceId!, e.SerialNumber!, e.EdgeDeviceId!, e.LinkedAtUtc!.Value),
                last == null ? null : new EquipmentElectricalSnapshot(last.CapturedAtUtc, last.Voltage, last.Current, last.PowerFactor,
                    last.Frequency, last.LeakageCurrentAmps, last.AnomalyType, last.Severity, last.RelativeHumidityPercent,
                    last.PanelSurfaceMoistureDetected),
                e.BindingId != null && receivedBy.TryGetValue(e.BindingId, out var at) ? at : null);
        }).ToList();
    }
}
