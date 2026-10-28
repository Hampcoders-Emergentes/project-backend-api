using Hampcoders.Electrolink.API.Processing.Domain.Model.Entities;
using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Processing.Domain.Services;

// Pure detection only. No persistence, alert lifecycle, relay or publisher dependencies.
public static class ElectricalAnomalyRules
{
    public static (EAnomalyType type, EAnomalySeverity severity)? Detect(
        Reading latest, IReadOnlyCollection<Reading> window, ThresholdConfig thresholds)
    {
        float nomV   = thresholds.NominalVoltage;
        float nomHz  = thresholds.NominalFrequency;

        if (latest.Current > thresholds.MaxCurrentAmps && latest.PowerFactor < 0.7f)
            return (EAnomalyType.ShortCircuitRisk, EAnomalySeverity.Critical);

        if (latest.Voltage > nomV * 1.20f)
            return (EAnomalyType.VoltageSpike, EAnomalySeverity.High);

        var last3 = window.OrderByDescending(r => r.Timestamp).Take(3).ToList();
        if (last3.Count == 3 && last3.All(r => r.Voltage < nomV * 0.85f))
            return (EAnomalyType.VoltageSag, EAnomalySeverity.Medium);

        var windowKw = window
            .Where(r => r.Timestamp >= DateTime.UtcNow.AddMinutes(-30))
            .Average(r => (double)r.ActivePowerKw);
        if (windowKw * 1000 > thresholds.MaxConsumptionWatts)
        {
            var severity = windowKw * 1000 > thresholds.MaxConsumptionWatts * 1.5f
                ? EAnomalySeverity.High : EAnomalySeverity.Medium;
            return (EAnomalyType.SustainedOverconsumption, severity);
        }

        var last5 = window.OrderByDescending(r => r.Timestamp).Take(5).ToList();
        if (last5.Count == 5 && last5.All(r => r.PowerFactor < thresholds.MinPowerFactor))
            return (EAnomalyType.PowerFactorDegradation, EAnomalySeverity.Low);

        if (last3.Count == 3 && last3.All(r => MathF.Abs(r.Frequency - nomHz) > 1f))
            return (EAnomalyType.AbnormalFrequency, EAnomalySeverity.Medium);

        return null;
    }

}
