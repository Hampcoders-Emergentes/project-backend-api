using Hampcoders.Electrolink.API.AlertManagement.Application;
using Hampcoders.Electrolink.API.StoreManagement.Interfaces.ACL;

namespace Hampcoders.Electrolink.API.AlertManagement.Infrastructure.Dispatch;

/// <summary>Anti-corruption adapter: reads the store's notification preferences through the StoreManagement ACL.</summary>
public sealed class StoreRoutingPolicyAdapter(IStoreNotificationRoutesFacade facade) : IAlertRoutingPolicyPort
{
    public async Task<IReadOnlyList<AlertRoute>> RoutesFor(string storeId, string severity, CancellationToken ct) =>
        (await facade.RoutesFor(storeId, severity, ct)).Select(r => new AlertRoute(r.Channel, r.Destination)).ToList();
}
