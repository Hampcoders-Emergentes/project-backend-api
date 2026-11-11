using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.StoreManagement.Domain.Model;

/// <summary>
///     Domain service: moisture near distribution boards. Water on the panel surface is critical (shock and short-circuit
///     risk); sustained relative humidity above the safe range is a warning (condensation, corrosion, tracking).
/// </summary>
public static class MoistureHazardPolicy
{
    public const string AnomalyType = "MoistureHazard";
    /// <summary>Relative humidity from which the environment of an electrical panel is no longer considered safe.</summary>
    public const float WarningRelativeHumidityPercent = 80f;
    /// <summary>Relative humidity from which condensation on live parts is likely.</summary>
    public const float SevereRelativeHumidityPercent = 90f;

    public static (string Type, EAnomalySeverity Severity)? Evaluate(float? relativeHumidityPercent, bool? panelSurfaceMoistureDetected)
    {
        if (panelSurfaceMoistureDetected == true) return (AnomalyType, EAnomalySeverity.Critical);
        return relativeHumidityPercent switch
        {
            >= SevereRelativeHumidityPercent => (AnomalyType, EAnomalySeverity.High),
            >= WarningRelativeHumidityPercent => (AnomalyType, EAnomalySeverity.Medium),
            _ => null
        };
    }

    /// <summary>Keeps the most severe of the electrical and moisture findings (electrical wins ties).</summary>
    public static (string Type, EAnomalySeverity Severity)? MostSevere((EAnomalyType type, EAnomalySeverity severity)? electrical,
        (string Type, EAnomalySeverity Severity)? moisture)
    {
        if (electrical == null) return moisture;
        if (moisture == null || moisture.Value.Severity <= electrical.Value.severity) return (electrical.Value.type.ToString(), electrical.Value.severity);
        return moisture;
    }
}
