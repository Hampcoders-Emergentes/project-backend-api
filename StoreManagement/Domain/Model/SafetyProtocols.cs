using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.StoreManagement.Domain.Model;

/// <summary>Conditions under which an equipment circuit counts as de-energized and safe for wet cleaning.</summary>
/// <param name="MaxDeEnergizedCurrentAmps">Current at or below this value counts as no load (sensor noise floor).</param>
/// <param name="MaxReadingAgeMinutes">Older telemetry cannot prove the circuit is off right now.</param>
/// <param name="RequireNoActiveCriticalAlerts">Active critical alerts on the equipment block cleaning.</param>
public sealed record SafeToCleanRule(float MaxDeEnergizedCurrentAmps, int MaxReadingAgeMinutes, bool RequireNoActiveCriticalAlerts)
{
    public static SafeToCleanRule Default() => new(0.05f, 2, true);

    public static SafeToCleanRule Create(float maxDeEnergizedCurrentAmps, int maxReadingAgeMinutes, bool requireNoActiveCriticalAlerts)
    {
        if (!float.IsFinite(maxDeEnergizedCurrentAmps) || maxDeEnergizedCurrentAmps is < 0 or > 1)
            throw new ArgumentException("maxDeEnergizedCurrentAmps must be between 0 and 1 A.");
        if (maxReadingAgeMinutes is < 1 or > 30) throw new ArgumentException("maxReadingAgeMinutes must be between 1 and 30.");
        return new(maxDeEnergizedCurrentAmps, maxReadingAgeMinutes, requireNoActiveCriticalAlerts);
    }
}

/// <summary>
///     Safe shutdown and emergency isolation procedure of one piece of equipment, plus the rule that decides whether it
///     may be cleaned. Equipment without a stored protocol uses <see cref="Default"/>.
/// </summary>
public sealed class EquipmentProtocol
{
    public const int MaxSteps = 30;
    public const int MaxStepLength = 300;

    public string EquipmentId { get; private set; } = null!;
    public string[] ShutdownSteps { get; private set; } = [];
    public string[] EmergencyIsolationSteps { get; private set; } = [];
    public SafeToCleanRule IsSafeToCleanRule { get; private set; } = SafeToCleanRule.Default();
    public DateTime? UpdatedAtUtc { get; private set; }
    public string? UpdatedByUserId { get; private set; }
    private EquipmentProtocol() { }

    public bool IsDefault => UpdatedAtUtc == null;

    public static EquipmentProtocol Default(string equipmentId) => new()
    {
        EquipmentId = equipmentId,
        ShutdownSteps =
        [
            "Stop the cooking or refrigeration cycle from the equipment's own controls and wait for moving parts to stop.",
            "Switch the equipment's dedicated breaker on the distribution board to OFF.",
            "Place a lockout padlock and a 'Do not energize' tag on the breaker.",
            "Unplug the equipment if it has a plug connection.",
            "Confirm on the Electrolink panel that the equipment draws no current before touching it."
        ],
        EmergencyIsolationSteps =
        [
            "Do not touch the equipment or anyone in contact with it.",
            "Switch off the equipment's breaker; if it cannot be identified, switch off the main breaker of the kitchen board.",
            "If there is fire, use a CO2 or class C extinguisher. Never use water on electrical equipment.",
            "Keep people away and call emergency services if anyone was injured.",
            "Report the incident in Electrolink and keep the circuit locked out until a technician inspects it."
        ],
        IsSafeToCleanRule = SafeToCleanRule.Default()
    };

    public void Update(IEnumerable<string> shutdownSteps, IEnumerable<string> isolationSteps, SafeToCleanRule rule, string actor, DateTime nowUtc)
    {
        ShutdownSteps = Steps(shutdownSteps, "shutdownSteps");
        EmergencyIsolationSteps = Steps(isolationSteps, "emergencyIsolationSteps");
        IsSafeToCleanRule = rule ?? throw new ArgumentNullException(nameof(rule));
        UpdatedAtUtc = nowUtc;
        UpdatedByUserId = actor;
    }

    private static string[] Steps(IEnumerable<string> steps, string field)
    {
        var list = (steps ?? []).Select(s => (s ?? "").Trim()).ToArray();
        if (list.Length is 0 or > MaxSteps || list.Any(s => s.Length is 0 or > MaxStepLength))
            throw new ArgumentException($"{field} must contain between 1 and {MaxSteps} steps of at most {MaxStepLength} characters.");
        return list;
    }
}

public static class CleaningSafetyStatus
{
    /// <summary>The circuit is de-energized and nothing else blocks cleaning.</summary>
    public const string SafeToClean = "SAFE_TO_CLEAN";
    /// <summary>The equipment still draws current.</summary>
    public const string Energized = "ENERGIZED";
    /// <summary>De-energized, but another hazard (leakage, moisture, critical alert) blocks cleaning.</summary>
    public const string Hazard = "HAZARD";
    /// <summary>No sensor or no recent telemetry: the state cannot be verified remotely.</summary>
    public const string Unverified = "UNVERIFIED";
}

public sealed record CleaningSafetyAssessment(string Status, bool IsDeEnergized, bool IsSafeToClean, IReadOnlyList<string> Reasons);

/// <summary>Domain service: decides from current telemetry whether an equipment may be cleaned (sanitation with water).</summary>
public static class SafeToCleanPolicy
{
    public static CleaningSafetyAssessment Evaluate(SafeToCleanRule rule, bool hasSensor, EquipmentElectricalSnapshot? last,
        ThresholdConfig thresholds, int activeCriticalAlerts, DateTime nowUtc)
    {
        if (!hasSensor)
            return new(CleaningSafetyStatus.Unverified, false, false,
                ["No sensor is linked to this equipment: apply the lockout procedure and verify with a tester before cleaning."]);
        if (last == null || nowUtc - last.CapturedAtUtc > TimeSpan.FromMinutes(rule.MaxReadingAgeMinutes))
            return new(CleaningSafetyStatus.Unverified, false, false,
                [$"No telemetry in the last {rule.MaxReadingAgeMinutes} minute(s): the circuit state cannot be confirmed."]);

        if (last.Current > rule.MaxDeEnergizedCurrentAmps)
            return new(CleaningSafetyStatus.Energized, false, false,
                [$"The equipment draws {last.Current:0.###} A (de-energized means at most {rule.MaxDeEnergizedCurrentAmps:0.###} A). Follow the shutdown protocol."]);

        var hazards = new List<string>();
        if (rule.RequireNoActiveCriticalAlerts && activeCriticalAlerts > 0)
            hazards.Add($"{activeCriticalAlerts} active critical alert(s) on this equipment.");
        if (last.LeakageCurrentAmps is { } leak && leak >= thresholds.NormalLimitAmps)
            hazards.Add($"Leakage current {leak:0.###} A is above the normal limit of {thresholds.NormalLimitAmps:0.###} A.");
        if (last.PanelSurfaceMoistureDetected == true)
            hazards.Add("Moisture detected on the electrical panel surface.");
        return hazards.Count > 0
            ? new(CleaningSafetyStatus.Hazard, true, false, hazards)
            : new(CleaningSafetyStatus.SafeToClean, true, true, ["The circuit is de-energized; the equipment may be cleaned."]);
    }
}

/// <summary>Step of the electrical incident quick guide.</summary>
public sealed record SafetyGuideSection(string Title, IReadOnlyList<string> Steps);

/// <summary>Emergency first-aid and isolation guide for electrical incidents, shown on the kitchen panel.</summary>
public static class ElectricalIncidentGuide
{
    public const string Version = "2026.1";

    public static readonly IReadOnlyList<SafetyGuideSection> Sections =
    [
        new("1. Make the area safe",
        [
            "Do not touch the injured person while they may still be in contact with the current.",
            "Switch off the equipment's breaker or the kitchen's main breaker on the distribution board.",
            "If the power cannot be cut, move the person away from the source with a dry, non-conductive object (wood, plastic).",
            "Keep everyone away from water on the floor near the equipment."
        ]),
        new("2. Call for help",
        [
            "Call the local emergency number and say it is an electrical injury.",
            "Notify the store manager and report the incident in Electrolink."
        ]),
        new("3. First aid",
        [
            "Check breathing. If the person is not breathing, start cardiopulmonary resuscitation (CPR) if trained.",
            "Use the store's automated external defibrillator (AED) if one is available.",
            "Cover burns with a clean, dry dressing. Do not apply ice, creams or butter.",
            "Keep the person warm and lying down until help arrives, even if they seem fine: electrical injuries can be internal."
        ]),
        new("4. Electrical fire",
        [
            "Cut the power first if it is safe to reach the breaker.",
            "Use a CO2 or class C extinguisher. Never use water on electrical equipment.",
            "Evacuate if the fire is not controlled within seconds."
        ]),
        new("5. After the incident",
        [
            "Keep the circuit locked out and tagged until a qualified technician has inspected it.",
            "Record what happened, who was present and the time; assign the alert to a technician in Electrolink."
        ])
    ];
}
