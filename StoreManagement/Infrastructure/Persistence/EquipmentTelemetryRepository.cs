using Hampcoders.Electrolink.API.StoreManagement.Application;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;

public sealed class EquipmentTelemetryRepository(StoreManagementDbContext context) : IEquipmentTelemetryRepository
{
    public Task<EquipmentDeviceBinding?> ByEquipment(string id, CancellationToken ct) => context.DeviceBindings.AsNoTracking().SingleOrDefaultAsync(b => b.EquipmentId == id, ct);
    public Task<EquipmentDeviceBinding?> ByDevice(string id, CancellationToken ct) => context.DeviceBindings.AsNoTracking().SingleOrDefaultAsync(b => b.DeviceId == id, ct);
    public Task<bool> HasOtherIdentity(string id, CancellationToken ct) => context.DeviceBindings.AnyAsync(b => b.DeviceId == id || b.SerialNumber == id, ct);
    public Task<EquipmentDeviceBinding?> ByEdgeId(string id, CancellationToken ct) => context.DeviceBindings.AsNoTracking().SingleOrDefaultAsync(b => b.EdgeDeviceId == id, ct);
    public Task<EquipmentMeasurement?> FindReading(string bindingId, string readingId, CancellationToken ct) =>
        context.Measurements.AsNoTracking().SingleOrDefaultAsync(r => r.BindingId == bindingId && r.ReadingId == readingId, ct);
    public Task<List<EquipmentMeasurement>> Recent(string bindingId, CancellationToken ct) => context.Measurements.AsNoTracking()
        .Where(r => r.BindingId == bindingId).OrderByDescending(r => r.CapturedAtUtc).ThenByDescending(r => r.ReadingId).Take(120).ToListAsync(ct);
    public Task<List<EquipmentMeasurement>> History(Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects.UserId actor,
        string storeId, string areaId, string equipmentId, DateTime fromUtc, DateTime toUtc, int skip, int take, CancellationToken ct) =>
        (from reading in context.Measurements.AsNoTracking()
         join binding in context.DeviceBindings.AsNoTracking() on reading.BindingId equals binding.Id
         join equipment in context.Equipment.AsNoTracking() on binding.EquipmentId equals equipment.Id
         join area in context.Areas.AsNoTracking() on equipment.AreaId equals area.Id
         join store in context.Stores.AsNoTracking() on area.StoreId equals store.Id
         where store.OwnerUserId == actor && binding.OwnerUserId == actor && store.Id == storeId
             && area.Id == areaId && equipment.Id == equipmentId
             && reading.CapturedAtUtc >= fromUtc && reading.CapturedAtUtc < toUtc
         select reading)
        .OrderBy(r => r.CapturedAtUtc).ThenBy(r => EF.Functions.Collate(r.ReadingId, "C"))
        .Skip(skip).Take(take).ToListAsync(ct);
    public void Add(EquipmentDeviceBinding binding) => context.DeviceBindings.Add(binding);
    public void Add(EquipmentMeasurement reading) => context.Measurements.Add(reading);
    public async Task Save(CancellationToken ct)
    {
        try { await context.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new EquipmentConflictException(); }
    }
}
