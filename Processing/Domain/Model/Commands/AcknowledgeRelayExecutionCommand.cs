using MediatR;

namespace Hampcoders.Electrolink.API.Processing.Domain.Model.Commands;

public record AcknowledgeRelayExecutionCommand(
    string  CommandId,
    string  DeviceId,
    bool    ExecutedSuccessfully,
    DateTime ExecutedAt
) : IRequest;
