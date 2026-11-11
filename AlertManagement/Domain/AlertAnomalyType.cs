namespace Hampcoders.Electrolink.API.AlertManagement.Domain;

/// <summary>
///     Alert vocabulary for anomalies, decoupled from the detector's internal names.
///     CURRENT_LEAKAGE, OVERCURRENT and VOLTAGE_SAG are the electrical hazards staff must react to first.
/// </summary>
public static class AlertAnomalyType
{
    public const string CurrentLeakage = "CURRENT_LEAKAGE";
    public const string Overcurrent = "OVERCURRENT";
    public const string VoltageSag = "VOLTAGE_SAG";
    public const string ShortCircuitRisk = "SHORT_CIRCUIT_RISK";
    public const string Overvoltage = "OVERVOLTAGE";
    public const string Overconsumption = "OVERCONSUMPTION";
    public const string LowPowerFactor = "LOW_POWER_FACTOR";
    public const string AbnormalFrequency = "ABNORMAL_FREQUENCY";
    public const string ConnectionLost = "CONNECTION_LOST";
    public const string UnusualConsumption = "UNUSUAL_CONSUMPTION";

    /// <summary>Maps a detector anomaly name (or an already normalized code) to the alert code.</summary>
    public static string FromDetection(string detected) => detected switch
    {
        "CurrentLeakage" or CurrentLeakage => CurrentLeakage,
        "Overcurrent" or Overcurrent => Overcurrent,
        "VoltageSag" or VoltageSag => VoltageSag,
        "ShortCircuitRisk" or ShortCircuitRisk => ShortCircuitRisk,
        "VoltageSpike" or Overvoltage => Overvoltage,
        "SustainedOverconsumption" or Overconsumption => Overconsumption,
        "PowerFactorDegradation" or LowPowerFactor => LowPowerFactor,
        "AbnormalFrequency" or AbnormalFrequency => AbnormalFrequency,
        "ConnectionLost" or ConnectionLost => ConnectionLost,
        "UnusualConsumptionPattern" or UnusualConsumption => UnusualConsumption,
        _ => throw new ArgumentException("Unknown anomaly type.")
    };
}
