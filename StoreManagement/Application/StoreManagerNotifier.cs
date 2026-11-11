using Hampcoders.Electrolink.API.Shared.Application.Notifications;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;

namespace Hampcoders.Electrolink.API.StoreManagement.Application;

/// <summary>Outbound port: pushes a store event to clients watching the store in real time.</summary>
public interface IStoreRealtimePort
{
    Task PublishAsync(string storeId, string eventName, object payload, CancellationToken ct);
}

/// <summary>A notice for the store manager, sent by email (configured EMAIL routes) and to the in-app realtime feed.</summary>
public sealed record StoreManagerNotice(string Subject, string Body, string RealtimeEvent, object RealtimePayload);

/// <summary>
///     Delivers operational notices (shift handovers, compliance expirations...) to the store manager. Email goes to every
///     enabled EMAIL route whose recipient role is STORE_MANAGER; the realtime event is always published. Best effort:
///     failures are logged, never thrown.
/// </summary>
public sealed class StoreManagerNotifier(IStoreNotificationPreferenceRepository preferences, IEmailSender email,
    IStoreRealtimePort realtime, ILogger<StoreManagerNotifier> logger)
{
    /// <returns>True when at least one channel accepted the notice.</returns>
    public async Task<bool> Notify(string storeId, StoreManagerNotice notice, CancellationToken ct)
    {
        var delivered = false;
        try
        {
            await realtime.PublishAsync(storeId, notice.RealtimeEvent, notice.RealtimePayload, ct);
            delivered = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[Store] Realtime notice {Event} failed for store {StoreId}", notice.RealtimeEvent, storeId);
        }

        var destinations = (await preferences.ListByStore(storeId, ct))
            .Where(p => p.Enabled && p.Channel == NotificationChannel.Email && p.RecipientRole == NotificationRecipientRole.StoreManager
                        && p.Destination != null)
            .Select(p => p.Destination!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var destination in destinations)
        {
            try
            {
                await email.SendAsync(new EmailMessage(destination, notice.Subject, notice.Body), ct);
                delivered = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "[Store] Email notice {Subject} could not be sent for store {StoreId}", notice.Subject, storeId);
            }
        }
        return delivered;
    }
}
