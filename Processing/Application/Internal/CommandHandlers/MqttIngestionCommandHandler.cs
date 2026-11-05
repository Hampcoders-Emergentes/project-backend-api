using Hampcoders.Electrolink.API.Processing.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Processing.Domain.Services;
using MediatR;

namespace Hampcoders.Electrolink.API.Processing.Application.Internal.CommandHandlers;

// In-process MediatR entry point for broker messages; the behaviour stays in the command services shared with the Edge HTTP API.
public class MqttIngestionCommandHandler(
    IDeviceReadingStreamCommandService streamService,
    IRelayCommandService relayService)
    : IRequestHandler<IngestDeviceReadingCommand, bool>,
      IRequestHandler<ReportEdgeAnomalyCommand, Unit>,
      IRequestHandler<MarkDeviceAsDisconnectedCommand, Unit>,
      IRequestHandler<AcknowledgeRelayExecutionCommand, Unit>
{
    public Task<bool> Handle(IngestDeviceReadingCommand request, CancellationToken cancellationToken)
        => streamService.Handle(request);

    public async Task<Unit> Handle(ReportEdgeAnomalyCommand request, CancellationToken cancellationToken)
    {
        await streamService.Handle(request);
        return Unit.Value;
    }

    public async Task<Unit> Handle(MarkDeviceAsDisconnectedCommand request, CancellationToken cancellationToken)
    {
        await streamService.Handle(request);
        return Unit.Value;
    }

    public async Task<Unit> Handle(AcknowledgeRelayExecutionCommand request, CancellationToken cancellationToken)
    {
        await relayService.Handle(request);
        return Unit.Value;
    }
}
