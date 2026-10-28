using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;

namespace Hampcoders.Electrolink.API.StoreManagement.Application;

public sealed record StoreResource(string Id, string Name, string Address, string OwnerUserId);
public sealed record AreaResource(string Id, string StoreId, string Name);
public sealed record EquipmentResource(string Id, string AreaId, string Name);
public sealed record HierarchyPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, bool HasMore);
public sealed class HierarchyNotFoundException() : Exception("Resource not found.");

public sealed class StoreHierarchyService(IStoreHierarchyRepository repository)
{
    public async Task<StoreResource> CreateStore(UserId actor, string name, string address, CancellationToken ct)
    {
        var store = Store.Register(actor, name, address);
        repository.Add(store);
        await repository.SaveAsync(ct);
        return Resource(store);
    }
    public async Task<StoreResource> GetStore(UserId actor, string storeId, CancellationToken ct) =>
        Resource(await RequireStore(actor, storeId, ct));

    public async Task<HierarchyPage<StoreResource>> ListStores(UserId actor, int page, int pageSize, CancellationToken ct)
    {
        var skip = Offset(page, pageSize);
        return Page(await repository.ListStoresAsync(actor, skip, pageSize + 1, ct), page, pageSize, Resource);
    }
    public async Task<AreaResource> CreateArea(UserId actor, string storeId, string name, CancellationToken ct)
    {
        await RequireStore(actor, storeId, ct);
        var area = StoreArea.Register(storeId, name);
        repository.Add(area);
        await repository.SaveAsync(ct);
        return Resource(area);
    }
    public async Task<AreaResource> GetArea(UserId actor, string storeId, string areaId, CancellationToken ct) =>
        Resource(await RequireArea(actor, storeId, areaId, ct));

    public async Task<HierarchyPage<AreaResource>> ListAreas(UserId actor, string storeId, int page, int pageSize, CancellationToken ct)
    {
        var skip = Offset(page, pageSize);
        await RequireStore(actor, storeId, ct);
        return Page(await repository.ListAreasAsync(actor, storeId, skip, pageSize + 1, ct), page, pageSize, Resource);
    }
    public async Task<EquipmentResource> CreateEquipment(UserId actor, string storeId, string areaId, string name, CancellationToken ct)
    {
        await RequireArea(actor, storeId, areaId, ct);
        var equipment = ElectricalEquipment.Register(areaId, name);
        repository.Add(equipment);
        await repository.SaveAsync(ct);
        return Resource(equipment);
    }
    public async Task<EquipmentResource> GetEquipment(UserId actor, string storeId, string areaId, string equipmentId, CancellationToken ct)
    {
        await RequireArea(actor, storeId, areaId, ct);
        HierarchyValue.Id(equipmentId, "equipment-");
        return Resource(await repository.FindEquipmentAsync(actor, storeId, areaId, equipmentId, ct)
            ?? throw new HierarchyNotFoundException());
    }
    public async Task<EquipmentResource> GetEquipmentInStore(UserId actor, string storeId, string equipmentId, CancellationToken ct)
    {
        await RequireStore(actor, storeId, ct);
        // An explicitly supplied incompatible equipment filter must not broaden the query.
        try { HierarchyValue.Id(equipmentId, "equipment-"); }
        catch (ArgumentException) { throw new HierarchyNotFoundException(); }
        return Resource(await repository.FindEquipmentInStoreAsync(actor, storeId, equipmentId, ct)
            ?? throw new HierarchyNotFoundException());
    }
    public async Task<HierarchyPage<EquipmentResource>> ListEquipment(UserId actor, string storeId, string areaId, int page, int pageSize, CancellationToken ct)
    {
        var skip = Offset(page, pageSize);
        await RequireArea(actor, storeId, areaId, ct);
        return Page(await repository.ListEquipmentAsync(actor, storeId, areaId, skip, pageSize + 1, ct), page, pageSize, Resource);
    }
    private async Task<Store> RequireStore(UserId actor, string id, CancellationToken ct)
    {
        HierarchyValue.Id(id, "store-");
        return await repository.FindStoreAsync(actor, id, ct) ?? throw new HierarchyNotFoundException();
    }
    private async Task<StoreArea> RequireArea(UserId actor, string storeId, string areaId, CancellationToken ct)
    {
        await RequireStore(actor, storeId, ct);
        HierarchyValue.Id(areaId, "area-");
        return await repository.FindAreaAsync(actor, storeId, areaId, ct) ?? throw new HierarchyNotFoundException();
    }
    private static int Offset(int page, int pageSize)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new ArgumentException("page must be positive; pageSize must be between 1 and 100; offset must fit Int32.");
        return (page - 1) * pageSize;
    }
    private static HierarchyPage<TResource> Page<TEntity, TResource>(List<TEntity> rows, int page, int size, Func<TEntity, TResource> map) =>
        new(rows.Take(size).Select(map).ToArray(), page, size, rows.Count > size);
    private static StoreResource Resource(Store s) => new(s.Id, s.Name, s.Address, s.OwnerUserId.Value);
    private static AreaResource Resource(StoreArea a) => new(a.Id, a.StoreId, a.Name);
    private static EquipmentResource Resource(ElectricalEquipment e) => new(e.Id, e.AreaId, e.Name);
}
