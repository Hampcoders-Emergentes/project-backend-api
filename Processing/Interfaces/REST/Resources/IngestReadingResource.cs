namespace Hampcoders.Electrolink.API.Processing.Interfaces.REST.Resources;

/// <param name="LeakageCurrentAmps">Residual (earth leakage) current, when the panel has a differential sensor.</param>
public record IngestReadingResource(
    string   DeviceId,
    string   ReadingId,
    DateTime Timestamp,
    float    Voltage,
    float    Current,
    float    PowerFactor,
    float    Frequency,
    string   Source,
    float?   LeakageCurrentAmps = null
);
