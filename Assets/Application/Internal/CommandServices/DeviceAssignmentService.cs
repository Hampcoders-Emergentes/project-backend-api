using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Interfaces.ACL;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
namespace Hampcoders.Electrolink.API.Assets.Application.Internal.CommandServices;

public sealed class DeviceAssignmentService(IIoTDeviceRepository devices, IPropertyRepository properties,
    IProfilesContextFacade profiles, IInstallationAssignmentReader installations, IIoTDeviceCommandService commands)
{
    public async Task<IoTDevice> Handle(AssignRequestedDeviceCommand command)
    {
        // Assignment has its own explicitly approved policy; inventory read/retirement do not grant this permission.
        var claims = await profiles.GetProfileClaimsAsync(command.Actor.Value);
        if (claims is not { ProfileStatus: "Active", BusinessRole: "Technician", RoleSubjectId: not null } profile
            || await profiles.GetTechnicianIdByUserIdAsync(command.Actor.Value) != profile.RoleSubjectId)
            throw new DeviceAssignmentUnavailableException();
        var property = await properties.FindByIdAsync(PropertyId.From(command.PropertyId));
        if (property is not { IsActive: true } || property.Status == EPropertyStatus.Archived
            || !await installations.IsAssignedInstallation(command.InstallationRequestId, property.Id.Value, profile.RoleSubjectId, property.Owner))
            throw new DeviceAssignmentUnavailableException();
        var device = await devices.FindByIdAsync(IoTDeviceId.From(command.DeviceId)) ?? throw new DeviceAssignmentUnavailableException();
        if (device.StockTechnicianId?.Value != profile.RoleSubjectId) throw new DeviceAssignmentUnavailableException();
        if (device.Status != EDeviceStatus.InStock || device.AssignedPropertyId != null || device.InstallationRequestId != null)
            throw new DeviceAssignmentConflictException();
        return await commands.Handle(new AssignDeviceToPropertyCommand(device.Id.Value, property.Id.Value, command.InstallationRequestId));
    }
}
