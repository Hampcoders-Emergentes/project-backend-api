using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
namespace Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;

public sealed record RecordAssignedDeviceInstallationCommand(UserId Actor, string DeviceId, string PropertyId,
    string TechnicianId, string FirmwareVersion, DateTime InstalledAt);
