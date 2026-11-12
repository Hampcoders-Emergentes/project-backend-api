using Hampcoders.Electrolink.API.Processing.Domain.Model.Entities;
using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Processing.Domain.Services;

// Pure detection only. No persistence, alert lifecycle, relay or publisher dependencies.
public static class ElectricalAnomalyRules
{
    /// <summary>
    ///     Full evaluation for a live reading: instantaneous rules first, then rules over the recent window.
    ///     <paramref name="leakageCurrentAmps"/> is the residual (earth leakage) current when the sensor reports it.
    /// </summary>
    public static (EAnomalyType type, EAnomalySeverity severity)? Detect(
        Reading latest, IReadOnlyCollection<Reading> window, ThresholdConfig thresholds, float? leakageCurrentAmps = null)
    {
        var instantaneous = DetectInstantaneous(latest, thresholds, leakageCurrentAmps);
        if (instantaneous != null) return instantaneous;

        float nomV   = thresholds.NominalVoltage;
        float nomHz  = thresholds.NominalFrequency;

        var last3 = window.OrderByDescending(r => r.Timestamp).Take(3).ToList();
        if (last3.Count == 3 && last3.All(r => r.Voltage < nomV * 0.85f))
            return (EAnomalyType.VoltageSag, EAnomalySeverity.Medium);

        var recent = window.Where(r => r.Timestamp >= DateTime.UtcNow.AddMinutes(-30)).ToList();
        if (recent.Count > 0)
        {
            var windowKw = recent.Average(r => (double)r.ActivePowerKw);
            if (windowKw * 1000 > thresholds.MaxConsumptionWatts)
            {
                var severity = windowKw * 1000 > thresholds.MaxConsumptionWatts * 1.5f
                    ? EAnomalySeverity.High : EAnomalySeverity.Medium;
                return (EAnomalyType.SustainedOverconsumption, severity);
            }
        }

        var last5 = window.OrderByDescending(r => r.Timestamp).Take(5).ToList();
        if (last5.Count == 5 && last5.All(r => r.PowerFactor < thresholds.MinPowerFactor))
            return (EAnomalyType.PowerFactorDegradation, EAnomalySeverity.Low);

        if (last3.Count == 3 && last3.All(r => MathF.Abs(r.Frequency - nomHz) > 1f))
            return (EAnomalyType.AbnormalFrequency, EAnomalySeverity.Medium);

        return null;
    }

    /// <summary>
    ///     Rules that need only the reading itself, so they also apply to readings replayed after an outage.
    ///     Leakage at or above the alert limit is the most dangerous condition (shock and fire hazard).
    /// </summary>
    public static (EAnomalyType type, EAnomalySeverity severity)? DetectInstantaneous(
        Reading latest, ThresholdConfig thresholds, float? leakageCurrentAmps = null)
    {
        if (leakageCurrentAmps is { } leakage && leakage >= thresholds.AlertLimitAmps)
            return (EAnomalyType.CurrentLeakage, EAnomalySeverity.Critical);

        if (latest.Current > thresholds.MaxCurrentAmps && latest.PowerFactor < 0.7f)
            return (EAnomalyType.ShortCircuitRisk, EAnomalySeverity.Critical);

        if (leakageCurrentAmps is { } minorLeakage && minorLeakage > thresholds.NormalLimitAmps)
            return (EAnomalyType.CurrentLeakage, EAnomalySeverity.High);

        if (latest.Current > thresholds.MaxCurrentAmps)
            return (EAnomalyType.Overcurrent, EAnomalySeverity.High);

        if (latest.Voltage > thresholds.NominalVoltage * 1.20f)
            return (EAnomalyType.VoltageSpike, EAnomalySeverity.High);

        return null;
    }
}
