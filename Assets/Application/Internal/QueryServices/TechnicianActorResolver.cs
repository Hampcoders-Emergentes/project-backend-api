using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Assets.Application.Internal.QueryServices;

// Technician subject of an authenticated actor: active IAM user with an active Technician profile whose subject
// Profiles links to that user (same rule as DeviceAssignmentService). Null for any other actor; AccessRole grants nothing.
public static class TechnicianActorResolver
{
    public static async Task<TechnicianId?> ResolveAsync(IProfilesContextFacade profiles, User actor)
    {
        if (actor.Status != EUserStatus.Active) return null;
        var claims = await profiles.GetProfileClaimsAsync(actor.Id.Value);
        if (claims is not { ProfileStatus: "Active", BusinessRole: "Technician", RoleSubjectId: not null } profile
            || await profiles.GetTechnicianIdByUserIdAsync(actor.Id.Value) != profile.RoleSubjectId)
            return null;
        return TechnicianId.From(profile.RoleSubjectId);
    }
}
