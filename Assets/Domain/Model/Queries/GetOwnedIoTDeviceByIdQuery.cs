using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
namespace Hampcoders.Electrolink.API.Assets.Domain.Model.Queries;

public record GetOwnedIoTDeviceByIdQuery(UserId Actor, string DeviceId);
