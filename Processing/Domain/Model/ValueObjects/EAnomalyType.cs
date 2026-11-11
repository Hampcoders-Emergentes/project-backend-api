namespace Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;

// Append only: stored by name in telemetry rows and by ordinal in legacy anomaly records.
public enum EAnomalyType
{
    VoltageSpike,
    VoltageSag,
    SustainedOverconsumption,
    ShortCircuitRisk,
    PowerFactorDegradation,
    AbnormalFrequency,
    ConnectionLost,
    UnusualConsumptionPattern,
    Overcurrent,
    CurrentLeakage
}
