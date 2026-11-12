using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Application;

namespace Hampcoders.Electrolink.API.StoreManagement.Interfaces.ACL;

/// <summary>Public ACL: store ownership checks for other contexts.</summary>
public interface IStoreAccessFacade
{
    /// <summary>False when the store does not exist, is not owned by the actor, or the identifier is malformed.</summary>
    Task<bool> CanManage(UserId actor, string storeId, CancellationToken ct);
}

public sealed class StoreAccessFacade(StoreHierarchyService hierarchy) : IStoreAccessFacade
{
    public async Task<bool> CanManage(UserId actor, string storeId, CancellationToken ct)
    {
        try { await hierarchy.GetStore(actor, storeId, ct); return true; }
        catch (Exception e) when (e is HierarchyNotFoundException or ArgumentException) { return false; }
    }
}
