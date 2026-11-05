using MediatR;

namespace Hampcoders.Electrolink.API.Processing.Domain.Model.Commands;

public record MarkDeviceAsDisconnectedCommand(string DeviceId) : IRequest;
