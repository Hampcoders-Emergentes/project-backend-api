using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
namespace Hampcoders.Electrolink.API.StoreManagement.Application;

public sealed record AuthorizedDevice(string DeviceId, string SerialNumber, string PropertyId, string ProfileId);
public interface IDeviceLinkAuthority
{
    Task<AuthorizedDevice?> Verify(UserId actor, string internalDeviceId, string edgeDeviceId);
}
public sealed class EquipmentAccessException() : Exception("Device access denied.");
public sealed class EquipmentConflictException() : Exception("Conflicting device link or reading identity.");
