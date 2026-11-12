using Hampcoders.Electrolink.API.AlertManagement.Application;
using Hampcoders.Electrolink.API.AlertManagement.Infrastructure.Realtime;
using Hampcoders.Electrolink.API.Shared.Application.Notifications;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Email;
using Microsoft.AspNetCore.SignalR;

namespace Hampcoders.Electrolink.API.AlertManagement.Infrastructure.Dispatch;

/// <summary>
///     Derives channel readiness from the providers actually registered at startup (the same decision the delivery code
///     uses), so the answer cannot drift from behaviour: EMAIL is log-only with <see cref="LoggingEmailSender"/>
///     (no Email:Smtp:Host), SMS with <see cref="LoggingSmsAlertChannel"/> (no Notifications:Sms:GatewayUrl).
///     Only booleans leave this class.
/// </summary>
public sealed class NotificationChannelReadinessAdapter(IEmailSender email, IEnumerable<IExternalAlertChannel> channels,
    IServiceProvider services) : INotificationChannelReadinessPort
{
    public IReadOnlyList<NotificationChannelReadiness> Describe()
    {
        var external = channels.ToList();
        var emailDelivered = email is not LoggingEmailSender;
        var smsDelivered = external.Any(c => c is HttpSmsGatewayAlertChannel);
        var pushDelivered = services.GetService<IHubContext<AlertsHub>>() != null;
        var webhookDelivered = external.Any(c => c is WebhookAlertChannel);
        return
        [
            Readiness("EMAIL", emailDelivered),
            Readiness("SMS", smsDelivered),
            Readiness("DASHBOARD_PUSH", pushDelivered),
            Readiness("WEBHOOK", webhookDelivered)
        ];
    }

    private static NotificationChannelReadiness Readiness(string channel, bool delivered) =>
        new(channel, delivered ? NotificationDeliveryMode.Delivered : NotificationDeliveryMode.LogOnly, delivered);
}
