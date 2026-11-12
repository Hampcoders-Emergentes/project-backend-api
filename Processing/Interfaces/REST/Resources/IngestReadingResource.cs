namespace Hampcoders.Electrolink.API.Processing.Interfaces.REST.Resources;

/// <param name="LeakageCurrentAmps">Residual (earth leakage) current, when the panel has a differential sensor.</param>
/// <param name="RelativeHumidityPercent">Relative humidity next to the distribution board, when the panel has a hygrometer.</param>
/// <param name="PanelSurfaceMoistureDetected">Water detected on the panel surface, when the panel has a moisture probe.</param>
public record IngestReadingResource(
    string   DeviceId,
    string   ReadingId,
    DateTime Timestamp,
    float    Voltage,
    float    Current,
    float    PowerFactor,
    float    Frequency,
    string   Source,
    float?   LeakageCurrentAmps = null,
    float?   RelativeHumidityPercent = null,
    bool?    PanelSurfaceMoistureDetected = null
);
