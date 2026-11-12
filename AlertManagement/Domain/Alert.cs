using System.Security.Cryptography;
using System.Text;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
namespace Hampcoders.Electrolink.API.AlertManagement.Domain;

public sealed record DetectedMeasurement(string BindingId, string ReadingId, string EquipmentId, UserId OwnerUserId,
    string AnomalyType, string DetectionSeverity, DateTime CapturedAtUtc, DateTime DetectedAtUtc);

/// <summary>Lifecycle of an alert. OPEN and ASSIGNED alerts are still active (nobody has closed the incident).</summary>
public static class AlertStatus
{
    public const string Open = "OPEN";
    public const string Acknowledged = "ACKNOWLEDGED";
    public const string Assigned = "ASSIGNED";
    public const string Resolved = "RESOLVED";
    public static readonly IReadOnlyList<string> All = [Open, Acknowledged, Assigned, Resolved];
    public static readonly IReadOnlyList<string> Active = [Open, Assigned];
}

/// <summary>Where the alert came from.</summary>
public static class AlertSource
{
    public const string Telemetry = "TELEMETRY";
    public const string ManualReport = "MANUAL_REPORT";
}

/// <summary>What a person on the kitchen floor noticed when reporting an anomaly by hand.</summary>
public static class ManualAnomalyCategory
{
    public const string AbnormalNoise = "ABNORMAL_NOISE";
    public const string Sparks = "SPARKS";
    public const string BurningSmell = "BURNING_SMELL";
    public const string Overheating = "OVERHEATING";
    public const string FlickeringLights = "FLICKERING_LIGHTS";
    public const string Smoke = "SMOKE";
    public const string Other = "OTHER";
    public static readonly IReadOnlyList<string> All = [AbnormalNoise, Sparks, BurningSmell, Overheating, FlickeringLights, Smoke, Other];
}

/// <summary>Person attributed to an alert (who was on duty, who reported it).</summary>
public sealed record WorkerAttribution(string WorkerCode, string WorkerName);

public sealed class Alert
{
    public string Id { get; private set; } = null!;
    /// <summary>Sensor binding and reading that triggered the alert; null for alerts reported by hand.</summary>
    public string? BindingId { get; private set; }
    public string? ReadingId { get; private set; }
    public string EquipmentId { get; private set; } = null!;
    public string OwnerUserId { get; private set; } = null!;
    public string Source { get; private set; } = AlertSource.Telemetry;
    public string AnomalyType { get; private set; } = null!;
    public string DetectionSeverity { get; private set; } = null!;
    public string Severity { get; private set; } = null!;
    public DateTime CapturedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string Status { get; private set; } = AlertStatus.Open;
    public string? ReportCategory { get; private set; }
    public string? Description { get; private set; }
    public string? ReportedByWorkerCode { get; private set; }
    public string? ReportedByWorkerName { get; private set; }
    public string? OnDutyWorkerCode { get; private set; }
    public string? OnDutyWorkerName { get; private set; }
    public string? AcknowledgedByUserId { get; private set; }
    public DateTime? AcknowledgedAtUtc { get; private set; }
    public string? AssignedTechnicianId { get; private set; }
    public string? AssignedTechnicianName { get; private set; }
    public string? AssignedByUserId { get; private set; }
    public DateTime? AssignedAtUtc { get; private set; }
    public string? ResolvedByUserId { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public string? ResolutionNotes { get; private set; }
    /// <summary>
    ///     Position of this alert's latest change in its store's change stream: strictly increasing per store and stamped
    ///     every time the alert is created or changes state, so clients can ask for what changed after a sequence.
    ///     0 for alerts last changed before sequencing existed.
    /// </summary>
    public long Sequence { get; private set; }
    private Alert() { }
    public bool IsCritical => Severity == "CRITICAL";
    public bool IsActive => AlertStatus.Active.Contains(Status);
    /// <summary>Critical earth leakage: clients must sound the local alarm immediately.</summary>
    public bool IsCriticalLeakage => IsCritical && AnomalyType == AlertAnomalyType.CurrentLeakage;

    public static Alert Open(DetectedMeasurement source)
    {
        var severity = source.DetectionSeverity switch { "Critical" => "CRITICAL", "Low" or "Medium" or "High" => "NON_CRITICAL",
            _ => throw new ArgumentException("Unknown detection severity.") };
        if (string.IsNullOrWhiteSpace(source.AnomalyType) || source.CapturedAtUtc.Kind != DateTimeKind.Utc || source.DetectedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A valid UTC detection is required.");
        // Length prefix makes the source pair unambiguous; the database also enforces source uniqueness.
        var key = source.BindingId.Length + ":" + source.BindingId + source.ReadingId;
        return new() { Id = "alert-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant(),
            BindingId = source.BindingId, ReadingId = source.ReadingId, EquipmentId = source.EquipmentId,
            OwnerUserId = source.OwnerUserId.Value, Source = AlertSource.Telemetry,
            AnomalyType = AlertAnomalyType.FromDetection(source.AnomalyType), DetectionSeverity = source.DetectionSeverity,
            Severity = severity, CapturedAtUtc = source.CapturedAtUtc, CreatedAtUtc = source.DetectedAtUtc };
    }

    /// <summary>
    ///     Anomaly seen or heard by staff (noise, sparks, smell...). Always NON_CRITICAL: a person must confirm the
    ///     hazard before it can escalate, and critical alerts sound alarms on every panel of the store.
    /// </summary>
    public static Alert ReportManually(string equipmentId, UserId owner, string category, string description,
        WorkerAttribution? reportedBy, DateTime nowUtc)
    {
        category = (category ?? "").Trim().ToUpperInvariant();
        if (!ManualAnomalyCategory.All.Contains(category))
            throw new ArgumentException("category must be one of " + string.Join(", ", ManualAnomalyCategory.All) + ".");
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 1000)
            throw new ArgumentException("description is required and must contain at most 1000 characters.");
        if (string.IsNullOrWhiteSpace(equipmentId)) throw new ArgumentException("equipmentId is required.");
        var now = Truncate(RequireUtc(nowUtc));
        return new()
        {
            Id = "alert-" + Guid.NewGuid().ToString("N"), EquipmentId = equipmentId, OwnerUserId = owner.Value,
            Source = AlertSource.ManualReport, AnomalyType = AlertAnomalyType.ManualReport, DetectionSeverity = "Medium",
            Severity = "NON_CRITICAL", CapturedAtUtc = now, CreatedAtUtc = now, ReportCategory = category,
            Description = description.Trim(), ReportedByWorkerCode = reportedBy?.WorkerCode, ReportedByWorkerName = reportedBy?.WorkerName
        };
    }

    /// <summary>Stamps the store-wide change sequence allocated when this change is persisted.</summary>
    public void AssignSequence(long sequence)
    {
        if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence), "Alert sequences start at 1.");
        Sequence = sequence;
    }

    /// <summary>Records who was working when the incident happened (shift roster), for accountability.</summary>
    public void AttributeOnDutyWorker(WorkerAttribution? worker)
    {
        OnDutyWorkerCode = worker?.WorkerCode;
        OnDutyWorkerName = worker?.WorkerName;
    }

    public void Acknowledge(UserId actor, DateTime now)
    {
        RequireOwner(actor);
        if (Status != AlertStatus.Open) return;
        Status = AlertStatus.Acknowledged; AcknowledgedByUserId = actor.Value;
        AcknowledgedAtUtc = Truncate(RequireUtc(now));
    }

    /// <summary>Hands an unresolved alert to a technician; reassigning replaces the previous technician.</summary>
    public void Assign(UserId actor, string technicianId, string? technicianName, DateTime now)
    {
        RequireOwner(actor);
        if (Status == AlertStatus.Resolved) throw new AlertStateException("A resolved alert cannot be assigned.");
        if (string.IsNullOrWhiteSpace(technicianId) || technicianId.Trim().Length > 100)
            throw new ArgumentException("technicianId is required and must contain at most 100 characters.");
        if (technicianName != null && (technicianName.Trim().Length == 0 || technicianName.Trim().Length > 120))
            throw new ArgumentException("technicianName must contain between 1 and 120 characters.");
        Status = AlertStatus.Assigned;
        AssignedTechnicianId = technicianId.Trim();
        AssignedTechnicianName = technicianName?.Trim();
        AssignedByUserId = actor.Value;
        AssignedAtUtc = Truncate(RequireUtc(now));
    }

    public void Resolve(UserId actor, string? notes, DateTime now)
    {
        RequireOwner(actor);
        if (Status == AlertStatus.Resolved) throw new AlertStateException("The alert is already resolved.");
        if (notes != null && notes.Trim().Length > 1000) throw new ArgumentException("notes must contain at most 1000 characters.");
        Status = AlertStatus.Resolved;
        ResolvedByUserId = actor.Value;
        ResolvedAtUtc = Truncate(RequireUtc(now));
        ResolutionNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    private void RequireOwner(UserId actor)
    {
        if (actor.Value != OwnerUserId) throw new ArgumentException("Actor does not own the alert.");
    }

    private static DateTime RequireUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : throw new ArgumentException("UTC time required.");

    // PostgreSQL timestamps keep microseconds.
    private static DateTime Truncate(DateTime value) => new(value.Ticks - value.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc);
}

/// <summary>The requested transition is not allowed in the alert's current status.</summary>
public sealed class AlertStateException(string message) : Exception(message);
