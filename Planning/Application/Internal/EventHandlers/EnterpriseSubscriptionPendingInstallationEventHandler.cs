using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Application.Internal.EventHandler;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Events;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.ACL;

namespace Hampcoders.Electrolink.API.Planning.Application.Internal.EventHandlers;

public class EnterpriseSubscriptionPendingInstallationEventHandler(
    IServiceRequestRepository serviceRequestRepository,
    IUnitOfWork unitOfWork,
    ISubscriptionContextFacade subscriptionContextFacade,
    ExternalProfilesService profilesService,
    ILogger<EnterpriseSubscriptionPendingInstallationEventHandler> logger)
    : IEventHandler<EnterpriseSubscriptionPendingInstallationEvent>
{
    public async Task Handle(EnterpriseSubscriptionPendingInstallationEvent @event, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "[Planning] EnterpriseSubscriptionPendingInstallation for Sub={SubId}, Devices={Count}",
            @event.SubscriptionId, @event.InitialDeviceCount);

        if (@event.InitialDeviceCount <= 0)
        {
            logger.LogWarning("[Planning] InitialDeviceCount is 0 — no IoT installation required");
            return;
        }

        // The request's client is the subscription owner's Company, resolved by Profiles; no placeholder client.
        var client = await profilesService.GetActiveCompanyClientByUserIdAsync(@event.UserId)
            ?? throw new InvalidOperationException("Enterprise installation requires the subscription owner's active Company profile.");

        var serviceRequest = ServiceRequest.CreateIoTInstallationRequest(
            subscriptionId: @event.SubscriptionId,
            userId: @event.UserId,
            client: client,
            requiredDeviceCount: @event.InitialDeviceCount,
            installationDeadline: @event.InstallationDeadlineAt);

        await serviceRequestRepository.AddAsync(serviceRequest);
        await unitOfWork.CompleteAsync();

        await subscriptionContextFacade.RecordInstallationServiceRequestAsync(
            @event.SubscriptionId, serviceRequest.RequestId.Value);

        logger.LogInformation("[Planning] IoT installation ServiceRequest created: {RequestId}",
            serviceRequest.RequestId.Value);
    }
}
