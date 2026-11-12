namespace Hampcoders.Electrolink.API.Processing.Domain.Model.Commands;

/// <summary>A reading cached by the edge panel while the store had no connection; Timestamp is the original capture time.</summary>
public sealed record OfflineReading(string DeviceId, string ReadingId, DateTime Timestamp, float Voltage, float Current,
    float PowerFactor, float Frequency, string Source, float? LeakageCurrentAmps, float? RelativeHumidityPercent = null,
    bool? PanelSurfaceMoistureDetected = null);

/// <summary>Replays, in order, the readings an edge panel buffered during a network outage.</summary>
public sealed record SyncOfflineReadingsBatchCommand(string? PanelId, IReadOnlyList<OfflineReading> Readings)
{
    public const int MaxReadings = 1000;
}
