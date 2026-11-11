using Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;

namespace Hampcoders.Electrolink.API.IAM.Infrastructure.Notifications;

/// <summary>
///     Initial adapter: no mail transport yet. Logs the reset link; the raw token is logged only in Development.
///     Replace with an SMTP/provider adapter registered against <see cref="IPasswordResetNotificationPort"/>.
/// </summary>
public sealed class LoggingPasswordResetNotifier(
    IHostEnvironment environment,
    IConfiguration configuration,
    ILogger<LoggingPasswordResetNotifier> logger) : IPasswordResetNotificationPort
{
    public Task SendAsync(string email, string resetToken, DateTime expiresAtUtc, CancellationToken ct = default)
    {
        if (environment.IsDevelopment())
        {
            var baseUrl = (configuration["PasswordReset:FrontendUrl"] ?? "http://localhost:4200").TrimEnd('/');
            logger.LogInformation("[IAM] (dev mail) reset link for {Email}: {Link} (expires {Expires:u})",
                email, $"{baseUrl}/reset-password?token={Uri.EscapeDataString(resetToken)}", expiresAtUtc);
        }
        else
        {
            logger.LogWarning("[IAM] Password reset issued for {Email} but no mail provider is configured.", email);
        }
        return Task.CompletedTask;
    }
}
