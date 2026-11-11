using System.Net;
using System.Net.Mail;
using Hampcoders.Electrolink.API.Shared.Application.Notifications;
using Microsoft.Extensions.Options;

namespace Hampcoders.Electrolink.API.Shared.Infrastructure.Email;

/// <summary>SMTP adapter (STARTTLS by default). Sends a text body, plus an HTML alternative when given.</summary>
public sealed class SmtpEmailSender(IOptions<EmailSettings> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var settings = options.Value;
        using var mail = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = message.Subject,
            Body = message.TextBody,
            IsBodyHtml = false
        };
        mail.To.Add(message.To);
        if (message.HtmlBody != null)
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, null, "text/html"));

        using var client = new SmtpClient(settings.Smtp.Host, settings.Smtp.Port)
        {
            EnableSsl = settings.Smtp.EnableSsl,
            Timeout = settings.Smtp.TimeoutSeconds * 1000,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        if (!string.IsNullOrEmpty(settings.Smtp.Username))
            client.Credentials = new NetworkCredential(settings.Smtp.Username, settings.Smtp.Password);
        try
        {
            await client.SendMailAsync(mail, ct);
            logger.LogInformation("[Email] Sent \"{Subject}\" via SMTP", message.Subject);
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException)
        {
            throw new EmailDeliveryException("SMTP delivery failed.", ex);
        }
    }
}
