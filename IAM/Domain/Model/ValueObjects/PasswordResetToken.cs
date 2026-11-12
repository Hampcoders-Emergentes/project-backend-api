using System.Security.Cryptography;
using System.Text;

namespace Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;

/// <summary>
///     Pending password reset. Only the SHA-256 hash of the secret is stored (<see cref="Value"/>), so a database leak
///     does not expose usable links. The secret is valid for <see cref="Lifetime"/> and is consumed on first use.
/// </summary>
public record PasswordResetToken
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    /// <summary>Lower-case hex SHA-256 of the secret sent to the user.</summary>
    public string Value { get; }
    public DateTime ExpiresAt { get; }

    private PasswordResetToken(string value, DateTime expiresAt)
    {
        Value = value;
        ExpiresAt = expiresAt;
    }

    /// <summary>Creates a token and returns the secret to deliver; the secret itself is never stored.</summary>
    public static (PasswordResetToken Token, string Secret) Issue(DateTime nowUtc)
    {
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (new PasswordResetToken(HashOf(secret), nowUtc + Lifetime), secret);
    }

    public static PasswordResetToken FromHash(string value, DateTime expiresAt) => new(value, expiresAt);

    public static string HashOf(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret ?? ""))).ToLowerInvariant();

    public bool IsValid(string secret, DateTime nowUtc) =>
        nowUtc < ExpiresAt && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Value), Encoding.ASCII.GetBytes(HashOf(secret)));
}
