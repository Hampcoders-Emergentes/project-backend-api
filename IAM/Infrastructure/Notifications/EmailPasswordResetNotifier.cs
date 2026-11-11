using System.Globalization;
using System.Net;
using Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Shared.Application.Notifications;

namespace Hampcoders.Electrolink.API.IAM.Infrastructure.Notifications;

/// <summary>
///     Formats the password reset email (text and HTML) with a link to the frontend reset page
///     ("PasswordReset:FrontendUrl", default http://localhost:4200) and hands it to the email port.
/// </summary>
public sealed class EmailPasswordResetNotifier(IEmailSender email, IConfiguration configuration) : IPasswordResetNotificationPort
{
    public Task SendAsync(string emailAddress, string resetToken, DateTime expiresAtUtc, CancellationToken ct = default)
    {
        var baseUrl = (configuration["PasswordReset:FrontendUrl"] ?? "http://localhost:4200").TrimEnd('/');
        var link = $"{baseUrl}/reset-password?token={Uri.EscapeDataString(resetToken)}";
        var minutes = Math.Max(1, (int)Math.Round((expiresAtUtc - DateTime.UtcNow).TotalMinutes));
        var expires = expiresAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
        var text = $"We received a request to reset your Electrolink password.\n\n" +
                   $"Open this link within {minutes} minutes (until {expires}) to choose a new password:\n{link}\n\n" +
                   "The link works only once. If you did not request this, ignore this email; your password stays the same.";
        var safeLink = WebUtility.HtmlEncode(link);
        var html = "<p>We received a request to reset your Electrolink password.</p>" +
                   $"<p><a href=\"{safeLink}\">Choose a new password</a></p>" +
                   $"<p>The link expires in {minutes} minutes ({expires}) and works only once.</p>" +
                   "<p>If you did not request this, ignore this email; your password stays the same.</p>";
        return email.SendAsync(new EmailMessage(emailAddress, "Reset your Electrolink password", text, html), ct);
    }
}
