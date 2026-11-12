using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Entities;

namespace Hampcoders.Electrolink.API.StoreManagement.Domain.Model;

public sealed class EquipmentDeviceBinding
{
    public string Id { get; private set; } = null!;
    public string EquipmentId { get; private set; } = null!;
    public UserId OwnerUserId { get; private set; } = null!;
    public string DeviceId { get; private set; } = null!;
    public string SerialNumber { get; private set; } = null!;
    public string EdgeDeviceId { get; private set; } = null!;
    public string LegacyPropertyId { get; private set; } = null!;
    public string AuthorizedProfileId { get; private set; } = null!;
    public DateTime LinkedAtUtc { get; private set; }
    private EquipmentDeviceBinding() { }
    public static EquipmentDeviceBinding Link(string equipmentId, UserId actor, string deviceId, string serial,
        string edgeId, string propertyId, string profileId) => new()
    {
        Id = "binding-" + Guid.NewGuid().ToString("N"), EquipmentId = HierarchyValue.Id(equipmentId, "equipment-"),
        OwnerUserId = actor, DeviceId = deviceId, SerialNumber = serial, EdgeDeviceId = edgeId,
        LegacyPropertyId = propertyId, AuthorizedProfileId = profileId, LinkedAtUtc = DateTime.UtcNow
    };
}

public sealed class EquipmentMeasurement
{
    public string BindingId { get; private set; } = null!;
    public string ReadingId { get; private set; } = null!;
    public DateTime CapturedAtUtc { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }
    public string TimestampBasis { get; private set; } = null!;
    public float Voltage { get; private set; }
    public float Current { get; private set; }
    public float PowerFactor { get; private set; }
    public float Frequency { get; private set; }
    public string Source { get; private set; } = null!;
    public string? AnomalyType { get; private set; }
    public string? Severity { get; private set; }
    /// <summary>Residual (earth leakage) current in amps; null when the panel has no differential sensor.</summary>
    public float? LeakageCurrentAmps { get; private set; }
    /// <summary>Relative humidity (%) measured next to the distribution board; null when the panel has no hygrometer.</summary>
    public float? RelativeHumidityPercent { get; private set; }
    /// <summary>Water detected on the panel surface by a moisture probe; null when the panel has no probe.</summary>
    public bool? PanelSurfaceMoistureDetected { get; private set; }
    private EquipmentMeasurement() { }
    public static EquipmentMeasurement Capture(EquipmentDeviceBinding binding, string readingId, DateTime capturedAt,
        float voltage, float current, float powerFactor, float frequency, string source, DateTime receivedAt,
        float? leakageCurrentAmps = null, float? relativeHumidityPercent = null, bool? panelSurfaceMoistureDetected = null)
    {
        if (relativeHumidityPercent is { } humidity && (!float.IsFinite(humidity) || humidity < 0 || humidity > 100))
            throw new ArgumentException("Relative humidity must be between 0 and 100 percent.");
        if (!float.IsFinite(voltage) || !float.IsFinite(current) || !float.IsFinite(powerFactor) || !float.IsFinite(frequency))
            throw new ArgumentException("Measurements must be finite.");
        if (leakageCurrentAmps is { } leakage && (!float.IsFinite(leakage) || leakage < 0))
            throw new ArgumentException("Leakage current must be a finite, non-negative number.");
        if (!Enum.GetNames<EReadingSource>().Contains(source, StringComparer.Ordinal)) throw new ArgumentException("Invalid reading source.");
        if (string.IsNullOrWhiteSpace(readingId) || readingId.Length > 100) throw new ArgumentException("Invalid readingId.");
        var utc = capturedAt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(capturedAt, DateTimeKind.Utc) : capturedAt.ToUniversalTime();
        if (utc == DateTime.MinValue || utc > receivedAt) throw new ArgumentException("Invalid capture time.");
        // PostgreSQL timestamps retain microseconds; normalize before persistence and replay comparison.
        utc = new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc);
        _ = Reading.Create(Processing.Domain.Model.ValueObjects.ReadingId.From(readingId), StreamId.From(binding.Id), utc,
            voltage, current, powerFactor, frequency, Enum.Parse<EReadingSource>(source));
        return new EquipmentMeasurement { BindingId = binding.Id, ReadingId = readingId, CapturedAtUtc = utc,
            ReceivedAtUtc = receivedAt, TimestampBasis = capturedAt.Kind == DateTimeKind.Unspecified ? "EDGE_UTC_ASSUMED" : "EXPLICIT_OFFSET",
            Voltage = voltage, Current = current, PowerFactor = powerFactor, Frequency = frequency, Source = source,
            LeakageCurrentAmps = leakageCurrentAmps, RelativeHumidityPercent = relativeHumidityPercent,
            PanelSurfaceMoistureDetected = panelSurfaceMoistureDetected };
    }
    public Reading AsReading() => Reading.Create(Processing.Domain.Model.ValueObjects.ReadingId.From(ReadingId),
        StreamId.From(BindingId), CapturedAtUtc, Voltage, Current, PowerFactor, Frequency, Enum.Parse<EReadingSource>(Source));
    public bool SamePayload(EquipmentMeasurement other) => CapturedAtUtc == other.CapturedAtUtc && Voltage == other.Voltage
        && Current == other.Current && PowerFactor == other.PowerFactor && Frequency == other.Frequency && Source == other.Source
        && LeakageCurrentAmps == other.LeakageCurrentAmps && RelativeHumidityPercent == other.RelativeHumidityPercent
        && PanelSurfaceMoistureDetected == other.PanelSurfaceMoistureDetected;
    public void RecordDetection(string? type, string? severity) { AnomalyType = type; Severity = severity; }
}
