using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Assets.Application.Internal.QueryServices;

// Property owner of an authenticated actor: active IAM user with an active, concordant HomeOwner/Company profile.
// Null for any other actor; AccessRole (Admin/SuperAdmin) grants nothing here.
public static class PropertyOwnerResolver
{
    public static async Task<ClientIdentity?> ResolveAsync(IProfilesContextFacade profiles, User actor)
    {
        if (actor.Status != EUserStatus.Active) return null;
        var claims = await profiles.GetProfileClaimsAsync(actor.Id.Value);
        if (claims is not { ProfileStatus: "Active", RoleSubjectId: not null } profile) return null;
        if (profile.BusinessRole == "HomeOwner"
            && await profiles.GetProfileIdByHomeownerIdAsync(profile.RoleSubjectId) == profile.ProfileId)
            return ClientIdentity.FromHomeowner(profile.RoleSubjectId);
        if (profile.BusinessRole == "Company"
            && await profiles.GetProfileIdByCompanyIdAsync(profile.RoleSubjectId) == profile.ProfileId)
            return ClientIdentity.FromCompany(profile.RoleSubjectId);
        return null;
    }
}
