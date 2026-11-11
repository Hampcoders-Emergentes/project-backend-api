namespace Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;

/// <summary>Outbound port: delivers a password-reset token to its owner (email, SMS, ...).</summary>
public interface IPasswordResetNotificationPort
{
    Task SendAsync(string email, string resetToken, DateTime expiresAtUtc, CancellationToken ct = default);
}
