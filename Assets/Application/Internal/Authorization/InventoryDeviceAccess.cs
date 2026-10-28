using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
namespace Hampcoders.Electrolink.API.Assets.Application.Internal.Authorization;

// Shared eligibility only after separate explicit approval for retirement (M3.7) and individual read (M3.8).
internal static class InventoryDeviceAccess
{
    internal static async Task<IoTDevice?> FindOwned(UserId actor, string deviceId, IIoTDeviceRepository devices,
        IPropertyRepository properties, IProfilesContextFacade profiles)
    {
        var claims = await profiles.GetProfileClaimsAsync(actor.Value);
        if (claims is not { ProfileStatus: "Active", RoleSubjectId: not null } profile) return null;
        ClientIdentity owner;
        if (profile.BusinessRole == "HomeOwner" && await profiles.IsHomeownerActiveAsync(profile.RoleSubjectId))
            owner = ClientIdentity.FromHomeowner(profile.RoleSubjectId);
        else if (profile.BusinessRole == "Company" && await profiles.IsCompanyActiveAsync(profile.RoleSubjectId))
            owner = ClientIdentity.FromCompany(profile.RoleSubjectId);
        else return null;
        var device = await devices.FindByIdAsync(IoTDeviceId.From(deviceId));
        if (device?.AssignedPropertyId == null) return null;
        var property = await properties.FindByIdAndOwnerAsync(device.AssignedPropertyId, owner);
        return property is { IsActive: true } && property.Status != EPropertyStatus.Archived ? device : null;
        // Intentionally no Installed requirement: an assigned retired device remains readable to its eligible owner.
    }
}
