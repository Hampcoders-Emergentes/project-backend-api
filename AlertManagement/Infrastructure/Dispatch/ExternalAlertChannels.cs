using Hampcoders.Electrolink.API.AlertManagement.Application;
using Hampcoders.Electrolink.API.Shared.Application.Notifications;

namespace Hampcoders.Electrolink.API.AlertManagement.Infrastructure.Dispatch;

public sealed class EmailAlertChannel(IEmailSender email) : IExternalAlertChannel
{
    public string Channel => "EMAIL";
    public Task DeliverAsync(string destination, ExternalAlertMessage message, CancellationToken ct) =>
        email.SendAsync(new EmailMessage(destination, message.Subject, message.Body), ct);
}

/// <summary>
///     SMS gateway placeholder: no SMS provider is integrated yet, so messages are written to the log
///     (phone number masked). Register a provider adapter for the "SMS" channel to deliver for real.
/// </summary>
public sealed class LoggingSmsAlertChannel(ILogger<LoggingSmsAlertChannel> logger) : IExternalAlertChannel
{
    public string Channel => "SMS";
    public Task DeliverAsync(string destination, ExternalAlertMessage message, CancellationToken ct)
    {
        var masked = destination.Length > 4 ? new string('*', destination.Length - 4) + destination[^4..] : "****";
        logger.LogWarning("[Alerts] SMS to {Phone} (no SMS provider configured): {Subject}", masked, message.Subject);
        return Task.CompletedTask;
    }
}
