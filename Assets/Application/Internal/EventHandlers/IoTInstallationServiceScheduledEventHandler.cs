using Hampcoders.Electrolink.API.Assets.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Events;
using MediatR;

namespace Hampcoders.Electrolink.API.Assets.Application.Internal.EventHandlers;

public class IoTInstallationServiceScheduledEventHandler
    : INotificationHandler<IoTInstallationServiceScheduledExternalEvent>
{
    private readonly IAssetsContextFacade _assetsContextFacade;
    private readonly ILogger<IoTInstallationServiceScheduledEventHandler> _logger;

    public IoTInstallationServiceScheduledEventHandler(
        IAssetsContextFacade assetsContextFacade,
        ILogger<IoTInstallationServiceScheduledEventHandler> logger)
    {
        _assetsContextFacade = assetsContextFacade;
        _logger = logger;
    }

    public async Task Handle(
        IoTInstallationServiceScheduledExternalEvent notification,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "IoTInstallationServiceScheduled received for property {PropertyId} | InstallationRequest {RequestId}. " +
            "Assigning first available IN_STOCK device.",
            notification.PropertyId, notification.InstallationServiceRequestId);

        await _assetsContextFacade.AssignAvailableDeviceToPropertyAsync(
            notification.PropertyId,
            notification.InstallationServiceRequestId);
    }
}
