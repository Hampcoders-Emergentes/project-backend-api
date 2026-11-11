using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Interfaces.ACL;

namespace Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;

/// <summary>Store ownership checks through the StoreManagement ACL.</summary>
public sealed class ExternalStoreAccessService(IStoreAccessFacade stores)
{
    public Task<bool> CanManage(UserId actor, string storeId, CancellationToken ct) => stores.CanManage(actor, storeId, ct);
}
