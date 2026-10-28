using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;

public sealed class StoreHierarchyRepository(StoreManagementDbContext context) : IStoreHierarchyRepository
{
    private IQueryable<Store> Stores(UserId actor) => context.Stores.AsNoTracking().Where(s => s.OwnerUserId == actor);
    private IQueryable<StoreArea> Areas(UserId actor, string storeId) =>
        from a in context.Areas.AsNoTracking()
        join s in Stores(actor) on a.StoreId equals s.Id
        where s.Id == storeId
        select a;
    private IQueryable<ElectricalEquipment> Equipment(UserId actor, string storeId, string areaId) =>
        from e in context.Equipment.AsNoTracking()
        join a in Areas(actor, storeId) on e.AreaId equals a.Id
        where a.Id == areaId
        select e;

    public Task<Store?> FindStoreAsync(UserId actor, string storeId, CancellationToken ct) =>
        Stores(actor).SingleOrDefaultAsync(s => s.Id == storeId, ct);
    public Task<StoreArea?> FindAreaAsync(UserId actor, string storeId, string areaId, CancellationToken ct) =>
        Areas(actor, storeId).SingleOrDefaultAsync(a => a.Id == areaId, ct);
    public Task<ElectricalEquipment?> FindEquipmentAsync(UserId actor, string storeId, string areaId, string equipmentId, CancellationToken ct) =>
        Equipment(actor, storeId, areaId).SingleOrDefaultAsync(e => e.Id == equipmentId, ct);
    public Task<ElectricalEquipment?> FindEquipmentInStoreAsync(UserId actor, string storeId, string equipmentId, CancellationToken ct) =>
        (from e in context.Equipment.AsNoTracking()
         join a in Areas(actor, storeId) on e.AreaId equals a.Id
         where e.Id == equipmentId
         select e).SingleOrDefaultAsync(ct);
    public Task<List<Store>> ListStoresAsync(UserId actor, int skip, int take, CancellationToken ct) =>
        Stores(actor).OrderBy(s => s.Id).Skip(skip).Take(take).ToListAsync(ct);
    public Task<List<StoreArea>> ListAreasAsync(UserId actor, string storeId, int skip, int take, CancellationToken ct) =>
        Areas(actor, storeId).OrderBy(a => a.Id).Skip(skip).Take(take).ToListAsync(ct);
    public Task<List<ElectricalEquipment>> ListEquipmentAsync(UserId actor, string storeId, string areaId, int skip, int take, CancellationToken ct) =>
        Equipment(actor, storeId, areaId).OrderBy(e => e.Id).Skip(skip).Take(take).ToListAsync(ct);
    public void Add(Store store) => context.Stores.Add(store);
    public void Add(StoreArea area) => context.Areas.Add(area);
    public void Add(ElectricalEquipment equipment) => context.Equipment.Add(equipment);
    public async Task SaveAsync(CancellationToken ct) => await context.SaveChangesAsync(ct);
}
