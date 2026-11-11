using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Microsoft.AspNetCore.SignalR;
namespace Hampcoders.Electrolink.API.AlertManagement.Infrastructure.Realtime;

/// <summary>
///     Hub at /hubs/alerts. The IAM middleware authenticates the connection (Bearer header, or the
///     access_token query string for WebSockets). Clients call JoinStore(storeId); ownership is checked server-side.
///     Server → client event: "ReceiveAlert" with the alert resource.
/// </summary>
public sealed class AlertsHub(StoreHierarchyService hierarchy) : Hub
{
    public static string Group(string storeId) => $"store:{storeId}";

    public async Task JoinStore(string storeId)
    {
        if (Context.GetHttpContext()?.Items["User"] is not User actor) throw new HubException("Unauthorized.");
        try { await hierarchy.GetStore(actor.Id, storeId, Context.ConnectionAborted); }
        catch (Exception e) when (e is HierarchyNotFoundException or ArgumentException) { throw new HubException("Store not found."); }
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(storeId), Context.ConnectionAborted);
    }

    public Task LeaveStore(string storeId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(storeId), Context.ConnectionAborted);
}

public sealed class SignalRAlertNotificationAdapter(IHubContext<AlertsHub> hub) : Application.IAlertNotificationPort
{
    public Task PublishAsync(string storeId, Application.AlertResource alert, CancellationToken ct) =>
        hub.Clients.Group(AlertsHub.Group(storeId)).SendAsync("ReceiveAlert", alert, ct);
}
