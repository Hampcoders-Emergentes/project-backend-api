using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;

namespace Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;

public interface IStoreHierarchyRepository
{
    Task<Store?> FindStoreAsync(UserId actor, string storeId, CancellationToken ct);
    Task<StoreArea?> FindAreaAsync(UserId actor, string storeId, string areaId, CancellationToken ct);
    Task<ElectricalEquipment?> FindEquipmentAsync(UserId actor, string storeId, string areaId, string equipmentId, CancellationToken ct);
    Task<ElectricalEquipment?> FindEquipmentInStoreAsync(UserId actor, string storeId, string equipmentId, CancellationToken ct);
    Task<List<Store>> ListStoresAsync(UserId actor, int skip, int take, CancellationToken ct);
    Task<List<StoreArea>> ListAreasAsync(UserId actor, string storeId, int skip, int take, CancellationToken ct);
    Task<List<ElectricalEquipment>> ListEquipmentAsync(UserId actor, string storeId, string areaId, int skip, int take, CancellationToken ct);
    void Add(Store store);
    void Add(StoreArea area);
    void Add(ElectricalEquipment equipment);
    Task SaveAsync(CancellationToken ct);
}
