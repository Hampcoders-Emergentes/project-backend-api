using System.Security.Cryptography;
using System.Text;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
namespace Hampcoders.Electrolink.API.AlertManagement.Domain;

public sealed record DetectedMeasurement(string BindingId, string ReadingId, string EquipmentId, UserId OwnerUserId,
    string AnomalyType, string DetectionSeverity, DateTime CapturedAtUtc, DateTime DetectedAtUtc);

public sealed class Alert
{
    public string Id { get; private set; } = null!;
    public string BindingId { get; private set; } = null!;
    public string ReadingId { get; private set; } = null!;
    public string EquipmentId { get; private set; } = null!;
    public string OwnerUserId { get; private set; } = null!;
    public string AnomalyType { get; private set; } = null!;
    public string DetectionSeverity { get; private set; } = null!;
    public string Severity { get; private set; } = null!;
    public DateTime CapturedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string Status { get; private set; } = "OPEN";
    public string? AcknowledgedByUserId { get; private set; }
    public DateTime? AcknowledgedAtUtc { get; private set; }
    private Alert() { }
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
            OwnerUserId = source.OwnerUserId.Value, AnomalyType = source.AnomalyType, DetectionSeverity = source.DetectionSeverity,
            Severity = severity, CapturedAtUtc = source.CapturedAtUtc, CreatedAtUtc = source.DetectedAtUtc };
    }
    public void Acknowledge(UserId actor, DateTime now)
    {
        if (actor.Value != OwnerUserId) throw new ArgumentException("Actor does not own the alert.");
        if (Status == "ACKNOWLEDGED") return;
        if (now.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC time required.");
        Status = "ACKNOWLEDGED"; AcknowledgedByUserId = actor.Value;
        AcknowledgedAtUtc = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc);
    }
}
