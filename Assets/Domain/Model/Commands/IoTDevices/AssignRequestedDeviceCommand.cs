using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
namespace Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;

public sealed record AssignRequestedDeviceCommand(UserId Actor, string DeviceId, string PropertyId, string InstallationRequestId);
