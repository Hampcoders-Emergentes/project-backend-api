using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
namespace Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;

// Actor comes from IAM on the server, never from the HTTP request body.
public record DecommissionOwnedDeviceCommand(UserId Actor, string DeviceId, string Reason);
