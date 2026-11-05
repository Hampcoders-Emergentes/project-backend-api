using MediatR;

namespace Hampcoders.Electrolink.API.Processing.Domain.Model.Commands;

public record ReportEdgeAnomalyCommand(
    string  DeviceId,
    string  AnomalyType,
    string  TriggerReadingId,
    string  EdgeAlertPayloadJson
) : IRequest;
