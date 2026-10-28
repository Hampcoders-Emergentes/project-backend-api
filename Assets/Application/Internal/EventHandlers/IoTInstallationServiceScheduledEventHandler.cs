using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Events;
using MediatR;

namespace Hampcoders.Electrolink.API.Assets.Application.Internal.EventHandlers;

public class IoTInstallationServiceScheduledEventHandler
    : INotificationHandler<IoTInstallationServiceScheduledExternalEvent>
{
    private readonly IIoTDeviceCommandService _deviceCommandService;
    private readonly ILogger<IoTInstallationServiceScheduledEventHandler> _logger;

    public IoTInstallationServiceScheduledEventHandler(
        IIoTDeviceCommandService deviceCommandService,
        ILogger<IoTInstallationServiceScheduledEventHandler> logger)
    {
        _deviceCommandService = deviceCommandService;
        _logger = logger;
    }

    public async Task Handle(
        IoTInstallationServiceScheduledExternalEvent notification,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.SelectedDeviceId))
            throw new ArgumentException("Explicit device selection is required.", nameof(notification.SelectedDeviceId));
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation(
            "IoTInstallationServiceScheduled received for property {PropertyId} | InstallationRequest {RequestId}. " +
            "Assigning explicitly selected device {DeviceId}.",
            notification.PropertyId, notification.InstallationServiceRequestId, notification.SelectedDeviceId);

        // Trusted in-process integration event; HTTP authorization belongs to DeviceAssignmentService.
        await _deviceCommandService.Handle(new AssignDeviceToPropertyCommand(notification.SelectedDeviceId,
            notification.PropertyId, notification.InstallationServiceRequestId));
    }
}
