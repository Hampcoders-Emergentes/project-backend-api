namespace Hampcoders.Electrolink.API.Shared.Application.Notifications;

public sealed record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody = null);

/// <summary>Outbound port for transactional email (password recovery, alert fallback, ...).</summary>
public interface IEmailSender
{
    /// <exception cref="EmailDeliveryException">The message could not be handed to the mail transport.</exception>
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

public sealed class EmailDeliveryException(string message, Exception? inner = null) : Exception(message, inner);
