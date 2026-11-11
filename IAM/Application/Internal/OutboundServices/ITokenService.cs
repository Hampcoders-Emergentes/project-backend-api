using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;

namespace Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;

/**
 * <summary>
 *     The token service interface
 * </summary>
 * <remarks>
 *     This interface is used to generate and validate JWT tokens
 * </remarks>
 */
public interface ITokenService
{
    /**
     * <summary>
     *     Generate a JWT token
     * </summary>
     * <param name="user">The user to generate the token for</param>
     * <param name="profileClaims">The optional profile claims for the user</param>
     * <returns>The generated token</returns>
     */
    string GenerateToken(User user,(string ProfileId, string ProfileStatus, string? BusinessRole, string? RoleSubjectId, string? SubscriptionTier)? profileClaims = null);

    /**
     * <summary>
     *     Validate a JWT token
     * </summary>
     * <param name="token">The token to validate</param>
     * <returns>The user id if the token is valid, null otherwise</returns>
     */
    Task<string?> ValidateToken(string token);

    /// <summary>Generates a new opaque, high-entropy refresh token value.</summary>
    string GenerateRefreshTokenValue();

    /// <summary>One-way hash used to persist and look up refresh tokens.</summary>
    string HashRefreshToken(string refreshToken);

    TimeSpan RefreshTokenLifetime { get; }
}
