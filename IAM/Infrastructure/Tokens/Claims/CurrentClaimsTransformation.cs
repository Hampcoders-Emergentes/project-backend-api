using System.Security.Claims;
using Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Queries;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;

namespace Hampcoders.Electrolink.API.IAM.Infrastructure.Tokens.Claims;

/// <summary>
///     Replaces the snapshot claims of a validated access token (access role, profileId, profileStatus, businessRole,
///     roleSubjectId, subscriptionTier) with the current values from IAM and Profiles. A token issued before a profile
///     completion, a role change or a tier change therefore authorizes against up-to-date state instead of failing with
///     401/403 until the client calls refresh-claims. Values are cached per user for <see cref="CacheLifetime"/> and evicted
///     immediately by the endpoints that change them (<see cref="RefreshesAccessTokenAttribute"/>).
/// </summary>
public sealed class CurrentClaimsTransformation(
    IUserQueryService users,
    ExternalProfilesService profiles,
    IMemoryCache cache) : IClaimsTransformation
{
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);
    private const string RefreshedMarker = "claims_refreshed";

    internal static readonly string[] SnapshotClaimTypes =
        [ClaimTypes.Role, "access_role", "profileId", "profileStatus", "businessRole", "roleSubjectId", "subscriptionTier"];

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity) return principal;
        if (identity.HasClaim(c => c.Type == RefreshedMarker)) return principal;
        var userId = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return principal;

        var current = await cache.GetOrCreateAsync(CacheKey(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
            entry.Size = 1;
            return await LoadAsync(userId);
        });
        // Unknown user: leave the token as is; the IAM middleware rejects it.
        if (current is null) return principal;

        var refreshed = new ClaimsIdentity(
            identity.Claims.Where(c => !SnapshotClaimTypes.Contains(c.Type, StringComparer.Ordinal)),
            identity.AuthenticationType, identity.NameClaimType, identity.RoleClaimType);
        refreshed.AddClaims(current);
        refreshed.AddClaim(new Claim(RefreshedMarker, "true"));
        return new ClaimsPrincipal(refreshed);
    }

    public static void Invalidate(IMemoryCache cache, string userId) => cache.Remove(CacheKey(userId));

    private async Task<List<Claim>?> LoadAsync(string userId)
    {
        var user = await users.Handle(new GetUserByIdQuery(userId));
        if (user is null) return null;

        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, user.Role.ToString()),
            new("access_role", user.Role.ToString())
        };
        // Same shape as TokenService.GenerateToken.
        if (await profiles.GetProfileClaimsAsync(userId) is { } profile)
        {
            claims.Add(new("profileId", profile.ProfileId));
            claims.Add(new("profileStatus", profile.ProfileStatus));
            claims.Add(new("businessRole", profile.BusinessRole ?? string.Empty));
            claims.Add(new("roleSubjectId", profile.RoleSubjectId ?? string.Empty));
            claims.Add(new("subscriptionTier", profile.SubscriptionTier ?? "FREE"));
        }
        return claims;
    }

    private static string CacheKey(string userId) => "current-claims:" + userId;
}
