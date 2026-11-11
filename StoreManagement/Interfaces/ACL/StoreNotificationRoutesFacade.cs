using Hampcoders.Electrolink.API.StoreManagement.Application;

namespace Hampcoders.Electrolink.API.StoreManagement.Interfaces.ACL;

public sealed record StoreNotificationRoute(string Channel, string? Destination);

/// <summary>Public ACL: effective alert routes of a store for a severity (defaults applied). Internal callers only.</summary>
public interface IStoreNotificationRoutesFacade
{
    Task<IReadOnlyList<StoreNotificationRoute>> RoutesFor(string storeId, string severity, CancellationToken ct);
}

public sealed class StoreNotificationRoutesFacade(StoreNotificationPreferenceService preferences) : IStoreNotificationRoutesFacade
{
    public async Task<IReadOnlyList<StoreNotificationRoute>> RoutesFor(string storeId, string severity, CancellationToken ct) =>
        (await preferences.EffectiveRoutes(storeId, severity, ct)).Select(r => new StoreNotificationRoute(r.Channel, r.Destination)).ToList();
}
