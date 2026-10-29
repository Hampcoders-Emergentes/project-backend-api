using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hampcoders.Electrolink.API.Assets.Infrastructure.Persistence.EFC.Repositories;

public class IoTDeviceRepository : BaseRepository<IoTDevice, IoTDeviceId>, IIoTDeviceRepository
{
    public IoTDeviceRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IoTDevice?> FindBySerialNumberAsync(SerialNumber serialNumber)
        => await Context.Set<IoTDevice>().FirstOrDefaultAsync(d => d.SerialNumber == serialNumber);

    public async Task<IEnumerable<IoTDevice>> FindByStatusAsync(EDeviceStatus status)
        => await Context.Set<IoTDevice>().Where(d => d.Status == status).ToListAsync();

    public async Task<IEnumerable<IoTDevice>> FindByPropertyIdAsync(PropertyId propertyId)
        => await Context.Set<IoTDevice>()
            .Where(d => d.AssignedPropertyId != null && d.AssignedPropertyId.Value == propertyId.Value)
            .ToListAsync();

    public async Task<int> CountActiveByOwnerIdAsync(string ownerId)
        => await Context.Set<IoTDevice>()
            .Where(d => d.Status == EDeviceStatus.Installed || d.Status == EDeviceStatus.Maintenance)
            .CountAsync();

    public async Task<Hampcoders.Electrolink.API.Assets.Domain.Model.Queries.TechnicianDeviceStockPage> GetTechnicianStockPageAsync(
        TechnicianId technician, int page, int pageSize)
    {
        var own = Context.Set<IoTDevice>().AsNoTracking().Where(d => d.StockTechnicianId == technician);
        var total = await own.CountAsync();
        var available = await own.CountAsync(d => d.Status == EDeviceStatus.InStock && d.AssignedPropertyId == null && d.InstallationRequestId == null);
        var items = await own.OrderBy(d => d.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new(items, total, available);
    }

    public async Task<IEnumerable<IoTDevice>> FindTechnicianStockAsync(TechnicianId technician, EDeviceStatus? status = null)
    {
        var own = Context.Set<IoTDevice>().AsNoTracking().Where(d => d.StockTechnicianId == technician);
        if (status.HasValue) own = own.Where(d => d.Status == status.Value);
        return await own.OrderBy(d => d.Id).ToListAsync();
    }
    public Task<int> CountTechnicianActiveDevicesAsync(TechnicianId technician) =>
        Context.Set<IoTDevice>().CountAsync(d => d.StockTechnicianId == technician
            && (d.Status == EDeviceStatus.Installed || d.Status == EDeviceStatus.Maintenance));
}
