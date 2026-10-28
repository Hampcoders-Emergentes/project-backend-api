using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Application;
namespace Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Services;

// Read-only bridge: explicit profile subject and legacy assignment, never StoreId == PropertyId.
public sealed class ExistingAssetsDeviceLinkAuthority(IProfilesContextFacade profiles, IIoTDeviceRepository devices,
    IPropertyRepository properties) : IDeviceLinkAuthority
{
    public async Task<AuthorizedDevice?> Verify(UserId actor, string internalDeviceId, string edgeDeviceId)
    {
        if (string.IsNullOrWhiteSpace(internalDeviceId) || !internalDeviceId.StartsWith("dev-", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(edgeDeviceId) || edgeDeviceId.Length > 200) return null;
        var claims = await profiles.GetProfileClaimsAsync(actor.Value);
        if (claims is not { ProfileStatus: "Active", RoleSubjectId: not null } profile) return null;
        ClientIdentity owner;
        if (profile.BusinessRole == "HomeOwner" && await profiles.IsHomeownerActiveAsync(profile.RoleSubjectId))
            owner = ClientIdentity.FromHomeowner(profile.RoleSubjectId);
        else if (profile.BusinessRole == "Company" && await profiles.IsCompanyActiveAsync(profile.RoleSubjectId))
            owner = ClientIdentity.FromCompany(profile.RoleSubjectId);
        else return null;
        var device = await devices.FindByIdAsync(IoTDeviceId.From(internalDeviceId));
        if (device is not { Status: EDeviceStatus.Installed, AssignedPropertyId: not null }) return null;
        if (edgeDeviceId != device.Id.Value && edgeDeviceId != device.SerialNumber.Value) return null;
        var serialMatch = await devices.FindBySerialNumberAsync(SerialNumber.From(edgeDeviceId));
        if (serialMatch != null && serialMatch.Id != device.Id) return null;
        if (edgeDeviceId.StartsWith("dev-", StringComparison.Ordinal))
        {
            var idMatch = await devices.FindByIdAsync(IoTDeviceId.From(edgeDeviceId));
            if (idMatch != null && idMatch.Id != device.Id) return null;
        }
        var property = await properties.FindByIdAndOwnerAsync(device.AssignedPropertyId, owner);
        if (property is not { IsActive: true } || property.Status == EPropertyStatus.Archived) return null;
        return new(device.Id.Value, device.SerialNumber.Value, device.AssignedPropertyId.Value, profile.ProfileId);
    }
}
