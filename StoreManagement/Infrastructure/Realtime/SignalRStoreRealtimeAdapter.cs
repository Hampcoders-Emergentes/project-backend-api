using Hampcoders.Electrolink.API.AlertManagement.Infrastructure.Realtime;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Microsoft.AspNetCore.SignalR;

namespace Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Realtime;

/// <summary>
///     Store events travel on the same hub and store group as alerts (/hubs/alerts, JoinStore), so a kitchen panel needs
///     a single connection. Events: "PanelPauseChanged", "ShiftHandover", "ComplianceNotice".
/// </summary>
public sealed class SignalRStoreRealtimeAdapter(IHubContext<AlertsHub> hub) : IStoreRealtimePort
{
    public Task PublishAsync(string storeId, string eventName, object payload, CancellationToken ct) =>
        hub.Clients.Group(AlertsHub.Group(storeId)).SendAsync(eventName, payload, ct);
}
