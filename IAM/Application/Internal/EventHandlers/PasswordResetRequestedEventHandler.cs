using Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Events;
using Hampcoders.Electrolink.API.Shared.Application.Internal.EventHandler;

namespace Hampcoders.Electrolink.API.IAM.Application.Internal.EventHandlers;

/// <summary>Delivers reset tokens through the outbound port; delivery failures never reach the caller.</summary>
public class PasswordResetRequestedEventHandler(
    IPasswordResetNotificationPort notifier,
    ILogger<PasswordResetRequestedEventHandler> logger) : IEventHandler<PasswordResetRequestedEvent>
{
    public async Task Handle(PasswordResetRequestedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            await notifier.SendAsync(notification.Email, notification.ResetToken, notification.ExpiresAt, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[IAM] Password reset notification failed for user {UserId}", notification.UserId);
        }
    }
}
