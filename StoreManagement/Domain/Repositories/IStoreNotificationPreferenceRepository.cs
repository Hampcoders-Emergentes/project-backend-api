using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;

namespace Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;

public interface IStoreNotificationPreferenceRepository
{
    /// <summary>Routes of the store; callers authorize the store first.</summary>
    Task<List<StoreNotificationPreference>> ListByStore(string storeId, CancellationToken ct);

    /// <summary>Atomically replaces every route of the store.</summary>
    Task ReplaceForStore(string storeId, IReadOnlyList<StoreNotificationPreference> routes, CancellationToken ct);
}
