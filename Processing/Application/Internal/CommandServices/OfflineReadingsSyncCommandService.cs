using Hampcoders.Electrolink.API.Processing.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Processing.Domain.Services;
using Hampcoders.Electrolink.API.StoreManagement.Application;

namespace Hampcoders.Electrolink.API.Processing.Application.Internal.CommandServices;

public sealed record OfflineReadingResult(string ReadingId, string Status, string? Reason);

/// <param name="SynchronizedAtUtc">Server time at which the batch was processed.</param>
public sealed record OfflineSyncResult(string? PanelId, int Received, int Accepted, int Duplicates, int Conflicts, int Rejected,
    DateTime SynchronizedAtUtc, IReadOnlyList<OfflineReadingResult> Results);

/// <summary>
///     Handles <see cref="SyncOfflineReadingsBatchCommand"/>: readings of sensors linked to store equipment are replayed in
///     one batch (idempotent per readingId, original capture time kept); readings of sensors still on the legacy device
///     stream go through the regular single-reading path.
/// </summary>
public sealed class OfflineReadingsSyncCommandService(EquipmentTelemetryService equipmentTelemetry,
    IDeviceReadingStreamCommandService legacyStreams, ILogger<OfflineReadingsSyncCommandService> logger)
{
    public async Task<OfflineSyncResult> Handle(SyncOfflineReadingsBatchCommand command, CancellationToken ct)
    {
        if (command.Readings == null || command.Readings.Count == 0 || command.Readings.Count > SyncOfflineReadingsBatchCommand.MaxReadings)
            throw new ArgumentException($"readings must contain between 1 and {SyncOfflineReadingsBatchCommand.MaxReadings} items.");
        if (command.Readings.Any(r => r == null || string.IsNullOrWhiteSpace(r.DeviceId) || string.IsNullOrWhiteSpace(r.ReadingId)))
            throw new ArgumentException("Every reading requires deviceId and readingId.");

        var outcomes = await equipmentTelemetry.IngestBatch(command.Readings.Select(r => new EdgeReadingInput(r.DeviceId, r.ReadingId,
            r.Timestamp, r.Voltage, r.Current, r.PowerFactor, r.Frequency, r.Source, r.LeakageCurrentAmps)).ToList(), ct);

        var results = new OfflineReadingResult[outcomes.Count];
        // Legacy streams keep their own ordering rules: replay them chronologically, one at a time.
        foreach (var index in Enumerable.Range(0, outcomes.Count).OrderBy(i => command.Readings[i].Timestamp))
        {
            var outcome = outcomes[index];
            results[index] = outcome.Status == EdgeReadingOutcome.Unbound
                ? await IngestLegacy(command.Readings[index])
                : new(outcome.ReadingId, outcome.Status, outcome.Reason);
        }

        var result = new OfflineSyncResult(command.PanelId, results.Length,
            results.Count(r => r.Status == EdgeReadingOutcome.Accepted), results.Count(r => r.Status == EdgeReadingOutcome.Duplicate),
            results.Count(r => r.Status == EdgeReadingOutcome.Conflict), results.Count(r => r.Status == EdgeReadingOutcome.Rejected),
            DateTime.UtcNow, results);
        logger.LogInformation("[Processing] Offline sync {PanelId}: {Accepted} accepted, {Duplicates} duplicate, {Conflicts} conflict, {Rejected} rejected",
            command.PanelId, result.Accepted, result.Duplicates, result.Conflicts, result.Rejected);
        return result;
    }

    private async Task<OfflineReadingResult> IngestLegacy(OfflineReading r)
    {
        try
        {
            var accepted = await legacyStreams.Handle(new IngestDeviceReadingCommand(r.DeviceId, r.ReadingId, r.Timestamp,
                r.Voltage, r.Current, r.PowerFactor, r.Frequency, r.Source));
            return new(r.ReadingId, accepted ? EdgeReadingOutcome.Accepted : EdgeReadingOutcome.Rejected, accepted ? null : "Reading rejected.");
        }
        catch (Exception e) when (e is KeyNotFoundException or ArgumentException or InvalidOperationException)
        {
            return new(r.ReadingId, EdgeReadingOutcome.Rejected, e.Message);
        }
    }
}
