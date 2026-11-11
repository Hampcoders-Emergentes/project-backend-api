using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.StoreManagement.Domain.Model;

/// <summary>Operational risk of one piece of equipment, as shown to store staff.</summary>
public static class EquipmentRiskLevel
{
    public const string Safe = "SAFE";
    public const string Warning = "WARNING";
    public const string CriticalRisk = "CRITICAL_RISK";
    public const string Disconnected = "DISCONNECTED";
    public const string Unknown = "UNKNOWN";

    /// <summary>Higher is worse; used to order equipment and to derive the store-wide status.</summary>
    public static int Rank(string level) => level switch
    {
        CriticalRisk => 4, Disconnected => 3, Warning => 2, Unknown => 1, _ => 0
    };
}

/// <summary>Latest electrical values of the equipment, as stored from telemetry.</summary>
public sealed record EquipmentElectricalSnapshot(DateTime CapturedAtUtc, float Voltage, float Current, float PowerFactor,
    float Frequency, float? LeakageCurrentAmps, string? AnomalyType, string? AnomalySeverity);

/// <summary>Everything the risk policy needs; no persistence or framework types.</summary>
public sealed record EquipmentRiskInput(bool HasSensor, ThresholdConfig Thresholds, EquipmentElectricalSnapshot? Last,
    int OpenCriticalAlerts, int OpenNonCriticalAlerts);

public sealed record EquipmentRiskAssessment(string Level, IReadOnlyList<string> Reasons);

/// <summary>
///     Domain service: derives the operational risk of equipment from its sensor state, its configured limits,
///     its last measurement and its unresolved alerts. Rules are evaluated from most to least severe.
/// </summary>
public static class EquipmentRiskPolicy
{
    public static EquipmentRiskAssessment Evaluate(EquipmentRiskInput input, DateTime nowUtc)
    {
        if (!input.HasSensor) return new(EquipmentRiskLevel.Unknown, ["No sensor is linked to this equipment."]);
        if (input.Last == null) return new(EquipmentRiskLevel.Unknown, ["The sensor has not reported telemetry yet."]);

        var t = input.Thresholds;
        var last = input.Last;
        if (nowUtc - last.CapturedAtUtc > TimeSpan.FromMinutes(t.DisconnectionThresholdMin))
            return new(EquipmentRiskLevel.Disconnected,
                [$"No telemetry for more than {t.DisconnectionThresholdMin} minute(s)."]);

        var critical = new List<string>();
        if (input.OpenCriticalAlerts > 0) critical.Add($"{input.OpenCriticalAlerts} open critical alert(s).");
        if (last.AnomalySeverity == nameof(EAnomalySeverity.Critical)) critical.Add($"Last reading flagged {last.AnomalyType} as critical.");
        if (last.LeakageCurrentAmps is { } leak && leak >= t.AlertLimitAmps)
            critical.Add($"Leakage current {leak:0.###} A reaches the alert limit of {t.AlertLimitAmps:0.###} A.");
        if (last.Current > t.MaxCurrentAmps && last.PowerFactor < 0.7f)
            critical.Add("Overcurrent with a very low power factor (short-circuit risk).");
        if (critical.Count > 0) return new(EquipmentRiskLevel.CriticalRisk, critical);

        var warnings = new List<string>();
        if (input.OpenNonCriticalAlerts > 0) warnings.Add($"{input.OpenNonCriticalAlerts} open non-critical alert(s).");
        if (last.AnomalyType != null) warnings.Add($"Last reading flagged {last.AnomalyType}.");
        if (last.LeakageCurrentAmps is { } minor && minor > t.NormalLimitAmps)
            warnings.Add($"Leakage current {minor:0.###} A is above the normal limit of {t.NormalLimitAmps:0.###} A.");
        if (last.Current > t.MaxCurrentAmps) warnings.Add($"Current {last.Current:0.##} A exceeds {t.MaxCurrentAmps:0.##} A.");
        if (last.Voltage > t.NominalVoltage * 1.10f || last.Voltage < t.NominalVoltage * 0.90f)
            warnings.Add($"Voltage {last.Voltage:0.#} V is outside ±10% of {t.NominalVoltage:0.#} V.");
        if (last.PowerFactor < t.MinPowerFactor) warnings.Add($"Power factor {last.PowerFactor:0.##} is below {t.MinPowerFactor:0.##}.");
        return warnings.Count > 0
            ? new(EquipmentRiskLevel.Warning, warnings)
            : new(EquipmentRiskLevel.Safe, ["Operating within configured limits."]);
    }
}
