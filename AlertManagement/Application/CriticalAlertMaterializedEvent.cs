using MediatR;

namespace Hampcoders.Electrolink.API.AlertManagement.Application;

/// <summary>Raised after a CRITICAL alert has been durably committed.</summary>
public sealed record CriticalAlertMaterializedEvent(string StoreId, AlertResource Alert) : INotification;

/// <summary>Outbound port: pushes an alert to whoever is watching the store (SignalR, push, ...).</summary>
public interface IAlertNotificationPort
{
    Task PublishAsync(string storeId, AlertResource alert, CancellationToken ct);
}

public sealed class CriticalAlertMaterializedEventHandler(IAlertNotificationPort port, ILogger<CriticalAlertMaterializedEventHandler> logger)
    : INotificationHandler<CriticalAlertMaterializedEvent>
{
    public async Task Handle(CriticalAlertMaterializedEvent notification, CancellationToken ct)
    {
        try { await port.PublishAsync(notification.StoreId, notification.Alert, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Delivery is best effort: the alert is already stored and available through the polling query.
            logger.LogError(ex, "[Alerts] Realtime delivery failed for alert {AlertId}", notification.Alert.Id);
        }
    }
}
