using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Shared.Application.Authorization;

public sealed record ActorSubject(string ProfileId, ClientIdentity? Client, TechnicianId? Technician)
{
    public bool IsClient(ClientIdentity owner) => Client is not null && Client == owner;
    public bool IsClient(string clientId) => Client is not null && Client.ClientId == clientId;
    public bool IsTechnician(TechnicianId technician) => Technician is not null && Technician == technician;
    public bool IsTechnician(string technicianId) => Technician is not null && Technician.Value == technicianId;
}

public static class ActorSubjectResolver
{
    // The JWT 'sub' holds a user id (us-...), so the profile and its role subject are resolved server-side through Profiles.
    // Null for an actor without an active, concordant profile; AccessRole (Admin/SuperAdmin) grants nothing here.
    public static async Task<ActorSubject?> ResolveAsync(IProfilesContextFacade profiles, string userId)
    {
        var claims = await profiles.GetProfileClaimsAsync(userId);
        if (claims is not { ProfileStatus: "Active", RoleSubjectId: not null } profile) return null;

        if (profile.BusinessRole == "HomeOwner"
            && await profiles.GetProfileIdByHomeownerIdAsync(profile.RoleSubjectId) == profile.ProfileId)
            return new ActorSubject(profile.ProfileId, ClientIdentity.FromHomeowner(profile.RoleSubjectId), null);

        if (profile.BusinessRole == "Company"
            && await profiles.GetProfileIdByCompanyIdAsync(profile.RoleSubjectId) == profile.ProfileId)
            return new ActorSubject(profile.ProfileId, ClientIdentity.FromCompany(profile.RoleSubjectId), null);

        if (profile.BusinessRole == "Technician"
            && await profiles.GetTechnicianIdByUserIdAsync(userId) == profile.RoleSubjectId)
            return new ActorSubject(profile.ProfileId, null, TechnicianId.From(profile.RoleSubjectId));

        return null;
    }
}
