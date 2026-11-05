using Hampcoders.Electrolink.API.Processing.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Entities;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Events;
using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Processing.Domain.Repositories;
using Hampcoders.Electrolink.API.Processing.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using MediatR;

namespace Hampcoders.Electrolink.API.Processing.Application.Internal.CommandServices;

public class DeviceReadingStreamCommandService(
    IDeviceReadingStreamRepository streamRepository,
    IAnomalyCommandService         anomalyService,
    IUnitOfWork                    unitOfWork,
    IMediator                      mediator,
    ILogger<DeviceReadingStreamCommandService> logger)
    : IDeviceReadingStreamCommandService
{
    public async Task<bool> Handle(IngestDeviceReadingCommand command)
    {
        var stream = await streamRepository.FindByDeviceIdAsync(command.DeviceId);
        if (stream is null)
        {
            logger.LogInformation("[IoT] Auto-creating stream for unknown device {DeviceId}.", command.DeviceId);
            stream = DeviceReadingStream.Create(
                DeviceId.From(command.DeviceId),
                PropertyId.From("prop-edge-default"),
                ClientIdentity.FromHomeowner("ho-edge-default"));
            await streamRepository.AddAsync(stream);
            await unitOfWork.CompleteAsync();
        }

        Reading reading;
        try
        {
            reading = Reading.Create(
                ReadingId.From(command.ReadingId),
                stream.StreamId,
                command.Timestamp,
                command.Voltage,
                command.Current,
                command.PowerFactor,
                command.Frequency,
                Enum.Parse<EReadingSource>(command.Source));
        }
        catch (ArgumentException ex)
        {
            stream.RejectReading(ReadingId.From(command.ReadingId), ex.Message);
            await PublishAndClearEvents(stream);
            return false;
        }

        bool ingested = stream.IngestReading(reading);
        if (!ingested)
        {
            logger.LogDebug("[IoT] Reading {ReadingId} ignored (duplicate or INACTIVE stream).", command.ReadingId);
            return false;
        }

        await streamRepository.UpdateAsync(stream);
        await unitOfWork.CompleteAsync();
        await PublishAndClearEvents(stream);

        // Awaited because the service shares this scope's DbContext, which is disposed with it; the reading is already saved.
        try
        {
            await anomalyService.Handle(new EvaluateReadingForAnomaliesCommand(
                stream.StreamId.Value, command.ReadingId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[IoT] Anomaly evaluation failed for reading {ReadingId}.", command.ReadingId);
        }

        return true;
    }

    public async Task Handle(ReportEdgeAnomalyCommand command)
    {
        var stream = await streamRepository.FindByDeviceIdAsync(command.DeviceId)
            ?? throw new InvalidOperationException($"Stream not found for device {command.DeviceId}.");

        if (!Enum.TryParse<EAnomalyType>(command.AnomalyType, true, out var anomalyType))
            throw new ArgumentException($"Unknown anomaly type: {command.AnomalyType}");

        await anomalyService.Handle(new EvaluateReadingForAnomaliesCommand(
            stream.StreamId.Value, command.TriggerReadingId));

        logger.LogWarning("[IoT] EdgeAnomaly reported — Device: {DeviceId} | Type: {Type}",
            command.DeviceId, command.AnomalyType);
    }

    public async Task Handle(ReplayBufferedReadingsCommand command)
    {
        var readingsByDevice = command.BufferedReadings
            .GroupBy(r => r.DeviceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var streams = new Dictionary<string, Domain.Model.Aggregates.DeviceReadingStream>();

        foreach (var deviceId in readingsByDevice.Keys)
        {
            var stream = await streamRepository.FindByDeviceIdAsync(deviceId);
            if (stream is not null)
                streams[deviceId] = stream;
        }

        int valid = 0;

        foreach (var (deviceId, readings) in readingsByDevice)
        {
            if (!streams.TryGetValue(deviceId, out var stream)) continue;

            foreach (var reading in readings)
            {
                try
                {
                    var r = Reading.Create(
                        ReadingId.From(reading.ReadingId),
                        stream.StreamId,
                        reading.Timestamp,
                        reading.Voltage,
                        reading.Current,
                        reading.PowerFactor,
                        reading.Frequency,
                        EReadingSource.EdgeBufferReplay);

                    if (stream.IngestReading(r))
                        valid++;
                }
                catch (ArgumentException ex)
                {
                    logger.LogWarning("[IoT] Replay reading {ReadingId} rejected: {Reason}",
                        reading.ReadingId, ex.Message);
                }
            }

            await streamRepository.UpdateAsync(stream);
        }

        await unitOfWork.CompleteAsync();

        await mediator.Publish(new BufferedReadingsReplayedEvent(
            command.DeviceId, command.BufferedReadings.Count, valid, DateTime.UtcNow));

        logger.LogInformation("[IoT] BufferReplay complete — Device: {DeviceId} | Valid: {Valid}/{Total}",
            command.DeviceId, valid, command.BufferedReadings.Count);
    }

    public async Task Handle(MarkDeviceAsDisconnectedCommand command)
    {
        var stream = await streamRepository.FindByDeviceIdAsync(command.DeviceId);
        if (stream is null) return;

        var sinceLastSeen = DateTime.UtcNow - stream.LastReceivedAt;
        var threshold     = stream.CustomThresholds.DisconnectionThresholdMin;

        if (sinceLastSeen.TotalMinutes >= threshold)
        {
            if (sinceLastSeen.TotalMinutes >= threshold && sinceLastSeen.TotalMinutes < threshold * 2)
                stream.MarkAsDegraded();
            else
                stream.MarkAsDisconnected();

            await streamRepository.UpdateAsync(stream);
            await unitOfWork.CompleteAsync();
            await PublishAndClearEvents(stream);
        }
    }

    public async Task Handle(UpdateCustomThresholdsCommand command)
    {
        var streams = await streamRepository.FindByOwnerAsync(ClientIdentity.FromOwnerId(command.OwnerId));

        foreach (var stream in streams)
        {
            var thresholds = ThresholdConfig.Create(
                command.NominalVoltage, command.MaxConsumptionWatts,
                command.MaxCurrentAmps, command.MinPowerFactor,
                command.NominalFrequency, command.DisconnectionThresholdMin,
                command.NormalLimitAmps, command.AlertLimitAmps);

            stream.UpdateCustomThresholds(thresholds);
            await streamRepository.UpdateAsync(stream);
        }

        await unitOfWork.CompleteAsync();
        logger.LogInformation("[IoT] ThresholdsUpdated for OwnerId: {OwnerId}", command.OwnerId);
    }

    public async Task Handle(PauseDeviceStreamCommand command)
    {
        var stream = await streamRepository.FindByDeviceIdAsync(command.DeviceId)
            ?? throw new KeyNotFoundException($"Stream not found for device {command.DeviceId}.");

        stream.Pause();
        await streamRepository.UpdateAsync(stream);
        await unitOfWork.CompleteAsync();
        await PublishAndClearEvents(stream);
    }

    public async Task Handle(ResumeDeviceStreamCommand command)
    {
        var stream = await streamRepository.FindByDeviceIdAsync(command.DeviceId)
            ?? throw new KeyNotFoundException($"Stream not found for device {command.DeviceId}.");

        stream.Resume();
        await streamRepository.UpdateAsync(stream);
        await unitOfWork.CompleteAsync();
        await PublishAndClearEvents(stream);
    }

    private async Task PublishAndClearEvents(DeviceReadingStream stream)
    {
        foreach (var domainEvent in stream.DomainEvents)
            await mediator.Publish(domainEvent, CancellationToken.None);
        stream.ClearDomainEvents();
    }
}
