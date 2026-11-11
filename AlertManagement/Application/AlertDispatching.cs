using Hampcoders.Electrolink.API.AlertManagement.Domain;
using MediatR;

namespace Hampcoders.Electrolink.API.AlertManagement.Application;

/// <summary>Raised after an alert (and its queued dispatches) has been durably committed.</summary>
public sealed record AlertRaisedEvent(string StoreId, AlertResource Alert) : INotification;

/// <summary>Alert route of a store: channel plus destination (null for the in-app feed).</summary>
public sealed record AlertRoute(string Channel, string? Destination);

/// <summary>Outbound port: effective alert routes of a store for one severity (store defaults applied).</summary>
public interface IAlertRoutingPolicyPort
{
    Task<IReadOnlyList<AlertRoute>> RoutesFor(string storeId, string severity, CancellationToken ct);
}

/// <summary>Outbound port: pushes an alert to whoever is watching the store in real time (SignalR).</summary>
public interface IAlertNotificationPort
{
    Task PublishAsync(string storeId, AlertResource alert, CancellationToken ct);
}

/// <summary>Human-readable description of an alert for external channels.</summary>
public sealed record ExternalAlertMessage(string Subject, string Body);

/// <summary>Outbound port: one external delivery channel (EMAIL, SMS).</summary>
public interface IExternalAlertChannel
{
    string Channel { get; }
    Task DeliverAsync(string destination, ExternalAlertMessage message, CancellationToken ct);
}

/// <summary>Routing port: decides where an alert goes and hands it to each channel.</summary>
public interface IAlertDispatcher
{
    /// <summary>Stages a dispatch for every external route not yet queued (the caller commits). Returns how many were staged.</summary>
    Task<int> PlanExternal(Alert alert, string storeId, CancellationToken ct);

    /// <summary>Pushes the alert to in-app clients when the store routes its severity there. Never throws.</summary>
    Task<bool> PublishRealtime(string storeId, AlertResource alert, CancellationToken ct);
}

public sealed class AlertDispatcher(IAlertRoutingPolicyPort routing, IAlertNotificationPort realtime,
    IAlertDispatchRepository dispatches, ILogger<AlertDispatcher> logger) : IAlertDispatcher
{
    public const string DashboardPush = "DASHBOARD_PUSH";

    public async Task<int> PlanExternal(Alert alert, string storeId, CancellationToken ct)
    {
        var routes = (await routing.RoutesFor(storeId, alert.Severity, ct))
            .Where(r => r.Channel != DashboardPush && !string.IsNullOrEmpty(r.Destination)).ToList();
        if (routes.Count == 0) return 0;
        var queued = await dispatches.RoutesOf(alert.Id, ct);
        var now = DateTime.UtcNow;
        var staged = 0;
        foreach (var route in routes.DistinctBy(r => (r.Channel, r.Destination)))
        {
            if (queued.Contains((route.Channel, route.Destination!))) continue;
            dispatches.Add(AlertDispatch.Queue(alert.Id, storeId, route.Channel, route.Destination!, now));
            staged++;
        }
        return staged;
    }

    public async Task<bool> PublishRealtime(string storeId, AlertResource alert, CancellationToken ct)
    {
        try
        {
            var routes = await routing.RoutesFor(storeId, alert.Severity, ct);
            if (routes.All(r => r.Channel != DashboardPush)) return false;
            await realtime.PublishAsync(storeId, alert, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best effort: the alert is stored and available through the active-alerts query.
            logger.LogError(ex, "[Alerts] Realtime delivery failed for alert {AlertId}", alert.Id);
            return false;
        }
    }
}

public sealed class AlertRaisedEventHandler(IAlertDispatcher dispatcher) : INotificationHandler<AlertRaisedEvent>
{
    public Task Handle(AlertRaisedEvent notification, CancellationToken ct) =>
        dispatcher.PublishRealtime(notification.StoreId, notification.Alert, ct);
}
