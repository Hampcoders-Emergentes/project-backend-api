using Hampcoders.Electrolink.API.Shared.Application.Notifications;

namespace Hampcoders.Electrolink.API.Shared.Infrastructure.Email;

/// <summary>
///     Gateway used when no SMTP host is configured. Development logs the full body (links included) so flows
///     can be exercised locally; other environments log only that a message was dropped, never its content.
/// </summary>
public sealed class LoggingEmailSender(IHostEnvironment environment, ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (environment.IsDevelopment())
            logger.LogInformation("[Email] (dev mail) to {To}: {Subject}\n{Body}", message.To, message.Subject, message.TextBody);
        else
            logger.LogWarning("[Email] No SMTP host configured (Email:Smtp:Host); \"{Subject}\" was not delivered.", message.Subject);
        return Task.CompletedTask;
    }
}
