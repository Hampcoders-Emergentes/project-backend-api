using System.Security.Claims;
using System.Text;
using Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Tokens.JWT.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Hampcoders.Electrolink.API.IAM.Infrastructure.Tokens.JWT.Services;

/**
 * <summary>
 *     The token service
 * </summary>
 * <remarks>
 *     This class is used to generate and validate tokens
 * </remarks>
 */
public class TokenService(IOptions<TokenSettings> tokenSettings, ILogger<TokenService> logger) : ITokenService
{
    private readonly TokenSettings _tokenSettings = tokenSettings.Value;

    /**
     * <summary>
     *     Generate a JWT token
     * </summary>
     * <param name="user">The user to generate the token for</param>
     * <param name="profileClaims">The optional profile claims for the user</param>
     * <returns>The generated token</returns>
     */
    public string GenerateToken(
        User user,
        (string ProfileId, string ProfileStatus, string? BusinessRole, string? RoleSubjectId, string? SubscriptionTier)? profileClaims = null)
    {
        var secret = _tokenSettings.Secret;
        var key = Encoding.ASCII.GetBytes(secret);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.Value),
            new(ClaimTypes.Email, user.Email.Value),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("access_role", user.Role.ToString())
        };

        if (profileClaims.HasValue)
        {
            claims.Add(new("profileId", profileClaims.Value.ProfileId));
            claims.Add(new("profileStatus", profileClaims.Value.ProfileStatus));
            claims.Add(new("businessRole", profileClaims.Value.BusinessRole   ?? string.Empty));
            claims.Add(new("roleSubjectId", profileClaims.Value.RoleSubjectId  ?? string.Empty));
            claims.Add(new("subscriptionTier", profileClaims.Value.SubscriptionTier ?? "FREE"));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _tokenSettings.Issuer,
            Audience = _tokenSettings.Audience,
            Expires = DateTime.UtcNow.AddMinutes(_tokenSettings.AccessTokenMinutes),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var tokenHandler = new JsonWebTokenHandler();
        return tokenHandler.CreateToken(tokenDescriptor);
    }

    /**
     * <summary>
     *     VerifyPassword token
     * </summary>
     * <param name="token">The token to validate</param>
     * <returns>The user id if the token is valid, null otherwise</returns>
     */
    public async Task<string?> ValidateToken(string token)
    {
        // If token is null or empty
        if (string.IsNullOrEmpty(token))
            // Return null 
            return null;
        // Otherwise, perform validation
        var tokenHandler = new JsonWebTokenHandler();
        try
        {
            var tokenValidationResult = await tokenHandler.ValidateTokenAsync(token, _tokenSettings.CreateValidationParameters());

            if (!tokenValidationResult.IsValid) return null;
            var jwtToken = (JsonWebToken)tokenValidationResult.SecurityToken;
            var userId = jwtToken.Claims.First(claim => claim.Type == ClaimTypes.NameIdentifier).Value;
            return userId;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Token validation failed");
            return null;
        }
    }

    public string GenerateRefreshTokenValue() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_tokenSettings.RefreshTokenDays);
}
