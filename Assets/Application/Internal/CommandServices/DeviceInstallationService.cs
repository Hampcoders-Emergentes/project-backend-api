using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Interfaces.ACL;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
namespace Hampcoders.Electrolink.API.Assets.Application.Internal.CommandServices;

public sealed class DeviceInstallationService(IIoTDeviceRepository devices, IPropertyRepository properties,
    IProfilesContextFacade profiles, IInstallationAssignmentReader installations, IIoTDeviceCommandService commands)
{
    public async Task<IoTDevice> Handle(RecordAssignedDeviceInstallationCommand command)
    {
        var claims = await profiles.GetProfileClaimsAsync(command.Actor.Value);
        if (claims is not { ProfileStatus: "Active", BusinessRole: "Technician", RoleSubjectId: not null } profile
            || command.TechnicianId != profile.RoleSubjectId
            || await profiles.GetTechnicianIdByUserIdAsync(command.Actor.Value) != profile.RoleSubjectId)
            throw new DeviceInstallationUnavailableException();
        var device = await devices.FindByIdAsync(IoTDeviceId.From(command.DeviceId));
        if (device?.AssignedPropertyId?.Value != command.PropertyId || device.InstallationRequestId == null)
            throw new DeviceInstallationUnavailableException();
        var property = await properties.FindByIdAsync(device.AssignedPropertyId);
        if (property is not { IsActive: true } || property.Status == EPropertyStatus.Archived
            || !await installations.IsAssignedInstallation(device.InstallationRequestId.Value, property.Id.Value, profile.RoleSubjectId, property.Owner))
            throw new DeviceInstallationUnavailableException();
        if (device.Status != EDeviceStatus.Assigned) throw new DeviceInstallationStateException();
        // Keep trusted internal commands/handlers unchanged; HTTP reaches them only after resource authorization.
        return await commands.Handle(new RecordDeviceInstallationCommand(command.DeviceId, property.Id.Value, profile.RoleSubjectId,
            device.InstallationRequestId.Value, command.FirmwareVersion, command.InstalledAt));
    }
}
