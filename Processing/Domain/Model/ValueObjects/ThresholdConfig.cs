namespace Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;

public record ThresholdConfig
{
    public float  NominalVoltage            { get; }
    public float  MaxConsumptionWatts       { get; }
    public float  MaxCurrentAmps            { get; }
    public float  MinPowerFactor            { get; }
    public float  NominalFrequency          { get; }
    public int    DisconnectionThresholdMin { get; }
    public float  NormalLimitAmps           { get; }
    public float  AlertLimitAmps            { get; }

    private ThresholdConfig(
        float nominalVoltage, float maxConsumptionWatts,
        float maxCurrentAmps, float minPowerFactor,
        float nominalFrequency, int disconnectionThresholdMin,
        float normalLimitAmps, float alertLimitAmps)
    {
        NominalVoltage            = nominalVoltage;
        MaxConsumptionWatts       = maxConsumptionWatts;
        MaxCurrentAmps            = maxCurrentAmps;
        MinPowerFactor            = minPowerFactor;
        NominalFrequency          = nominalFrequency;
        DisconnectionThresholdMin = disconnectionThresholdMin;
        NormalLimitAmps           = normalLimitAmps;
        AlertLimitAmps            = alertLimitAmps;
    }

    public static ThresholdConfig Default() =>
        new(220f, 5000f, 20f, 0.85f, 60f, 10, 0.20f, 0.60f);

    public static ThresholdConfig Create(
        float nominalVoltage, float maxConsumptionWatts,
        float maxCurrentAmps, float minPowerFactor,
        float nominalFrequency, int disconnectionThresholdMin,
        float normalLimitAmps = 0.20f, float alertLimitAmps = 0.60f)
    {
        if (!float.IsFinite(nominalVoltage) || !float.IsFinite(maxConsumptionWatts) || !float.IsFinite(maxCurrentAmps)
            || !float.IsFinite(minPowerFactor) || !float.IsFinite(nominalFrequency)
            || !float.IsFinite(normalLimitAmps) || !float.IsFinite(alertLimitAmps))
            throw new ArgumentException("Thresholds must be finite numbers.");
        if (maxCurrentAmps <= 0)
            throw new ArgumentException("Max current must be positive.");
        if (nominalFrequency <= 0)
            throw new ArgumentException("Nominal frequency must be positive.");
        if (nominalVoltage <= 0)
            throw new ArgumentException("Nominal voltage must be positive.");
        if (maxConsumptionWatts <= 0)
            throw new ArgumentException("Max consumption must be positive.");
        if (minPowerFactor < 0 || minPowerFactor > 1)
            throw new ArgumentException("Power factor must be between 0 and 1.");
        if (disconnectionThresholdMin < 1)
            throw new ArgumentException("Disconnection threshold must be at least 1 minute.");
        if (normalLimitAmps <= 0)
            throw new ArgumentException("Normal limit must be positive.");
        if (alertLimitAmps <= normalLimitAmps)
            throw new ArgumentException("Alert limit must be greater than normal limit.");

        return new ThresholdConfig(
            nominalVoltage, maxConsumptionWatts,
            maxCurrentAmps, minPowerFactor,
            nominalFrequency, disconnectionThresholdMin,
            normalLimitAmps, alertLimitAmps);
    }
}
