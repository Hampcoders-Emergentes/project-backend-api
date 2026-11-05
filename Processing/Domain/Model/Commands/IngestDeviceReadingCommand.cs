using MediatR;

namespace Hampcoders.Electrolink.API.Processing.Domain.Model.Commands;

public record IngestDeviceReadingCommand(
    string  DeviceId,
    string  ReadingId,
    DateTime Timestamp,
    float   Voltage,
    float   Current,
    float   PowerFactor,
    float   Frequency,
    string  Source
) : IRequest<bool>;
