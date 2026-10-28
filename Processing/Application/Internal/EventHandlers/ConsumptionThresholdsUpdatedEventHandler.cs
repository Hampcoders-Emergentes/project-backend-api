using Hampcoders.Electrolink.API.Analytics.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Processing.Domain.Repositories;
using Hampcoders.Electrolink.API.Processing.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using MediatR;

namespace Hampcoders.Electrolink.API.Processing.Application.Internal.EventHandlers;

public class ConsumptionThresholdsUpdatedEventHandler(
    IDeviceReadingStreamCommandService commandService,
    IDeviceReadingStreamRepository     streamRepository,
    IUnitOfWork                        unitOfWork,
    ILogger<ConsumptionThresholdsUpdatedEventHandler> logger)
    : INotificationHandler<ConsumptionThresholdsUpdatedIntegrationEvent>
{
    private const string DemoDeviceId = "dev-electrolink-esp32-01";

    public async Task Handle(ConsumptionThresholdsUpdatedIntegrationEvent notification, CancellationToken cancellationToken)
    {
        var t = notification.Thresholds;
        logger.LogInformation("[IoT] ThresholdsUpdated event received for OwnerId: {OwnerId}, thresholdCount: {Count}",
            notification.OwnerId, t.Count);

        var normalLimit  = t.TryGetValue("normalLimitAmps", out var nla) ? (float)nla : 0.20f;
        var alertLimit   = t.TryGetValue("alertLimitAmps", out var ala) ? (float)ala : 0.60f;
        var nominalVolt  = t.TryGetValue("nominalVoltage", out var nv) ? (float)nv : 220f;
        var maxConsumpt  = t.TryGetValue("maxConsumptionWatts", out var mcw) ? (float)mcw : 5000f;
        var maxCurrent   = t.TryGetValue("maxCurrentAmps", out var mca) ? (float)mca : 20f;
        var minPF        = t.TryGetValue("minPowerFactor", out var mpf) ? (float)mpf : 0.85f;
        var nominalFreq  = t.TryGetValue("nominalFrequency", out var nf) ? (float)nf : 60f;
        var disconnectM  = t.TryGetValue("disconnectionThresholdMin", out var dtm) ? (int)dtm : 10;

        // 1. Update streams for the owner (existing flow)
        await commandService.Handle(new UpdateCustomThresholdsCommand(
            notification.OwnerId,
            nominalVolt, maxConsumpt, maxCurrent, minPF,
            nominalFreq, disconnectM, normalLimit, alertLimit));

        // 2. Also update the demo device stream so the Expo/Wokwi ESP32 picks it up
        var demoStream = await streamRepository.FindByDeviceIdAsync(DemoDeviceId);
        if (demoStream is not null && demoStream.Owner == ClientIdentity.FromOwnerId(notification.OwnerId))
        {
            var demoThresholds = ThresholdConfig.Create(
                nominalVolt, maxConsumpt, maxCurrent, minPF,
                nominalFreq, disconnectM, normalLimit, alertLimit);
            demoStream.UpdateCustomThresholds(demoThresholds);
            await streamRepository.UpdateAsync(demoStream);
            await unitOfWork.CompleteAsync();
            logger.LogInformation("[IoT] Demo device {DemoDeviceId} thresholds updated to match owner {OwnerId}.",
                DemoDeviceId, notification.OwnerId);
        }
    }
}
