using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.ACL;

namespace Hampcoders.Electrolink.API.Subscriptions.Application.Internal.OutboundServices;

public class SubscriptionProfileResolver(
    IProfilesContextFacade profilesContextFacade)
    : ISubscriptionProfileResolver
{
    public async Task<BusinessRole> ResolveBusinessRoleAsync(string userId)
    {
        var claims = await profilesContextFacade.GetProfileClaimsAsync(userId);
        if (claims is null || string.IsNullOrWhiteSpace(claims.Value.BusinessRole))
            throw new InvalidOperationException($"Profile not found or has no role for user {userId}");

        return BusinessRole.From(claims.Value.BusinessRole);
    }

    public async Task<string?> ResolveProfileFullNameAsync(string userId)
    {
        var claims = await profilesContextFacade.GetProfileClaimsAsync(userId);
        if (claims is null) return null;

        return await profilesContextFacade.GetProfileFullNameAsync(claims.Value.ProfileId);
    }

    public async Task<string?> ResolveSubscriptionProfileIdAsync(string userId)
        => await profilesContextFacade.GetProfileClaimsAsync(userId) is { ProfileStatus: "Active" or "Suspended" } claims
            ? claims.ProfileId : null;
}
