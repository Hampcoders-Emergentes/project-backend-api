using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Planning.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;

namespace Hampcoders.Electrolink.API.Planning.Application.Internal.CommandServices;

public enum EInstallationMatchingOutcome { Assigned, NoCandidates }

// Subscription installation: the owner's Company attaches its property, then the existing matching runs.
// No request-quota check: the installation is part of the paid Enterprise subscription.
public sealed class SubscriptionInstallationPropertyService(
    IServiceRequestRepository requests,
    ExternalProfilesService profiles,
    ExternalAssetsService assets,
    IUnitOfWork unitOfWork,
    IServiceAssignmentCommandService assignments)
{
    public async Task<(ServiceRequest Request, EInstallationMatchingOutcome Matching)> Handle(AttachInstallationPropertyCommand command)
    {
        var client = await profiles.GetActiveCompanyClientByUserIdAsync(command.Actor.Value)
            ?? throw new InstallationPropertyActorDeniedException();
        RequestId requestId;
        try { requestId = RequestId.From(command.RequestId); }
        catch (ArgumentException) { throw new InstallationRequestUnavailableException(); }
        var request = await requests.FindByIdAsync(requestId);
        if (request is null || request.SubscriptionId is null || request.UserId != command.Actor.Value || request.Client != client)
            throw new InstallationRequestUnavailableException();
        if (request.Status != ERequestStatus.PendingInstallation || request.PropertyId is not null)
            throw new InstallationRequestConflictException();
        var geolocation = await assets.GetActiveOwnedPropertyGeolocationAsync(command.PropertyId, client)
            ?? throw new InstallationRequestUnavailableException();

        request.AttachInstallationProperty(PropertyId.From(command.PropertyId),
            Geolocation.Create(geolocation.Latitude, geolocation.Longitude, null, "PROPERTY"));
        requests.Update(request);
        // Saved before matching: a later matching failure leaves the request persisted as PendingAssignment.
        await unitOfWork.CompleteAsync();

        try { await assignments.Handle(new ExecuteMatchingAlgorithmCommand(request.RequestId)); }
        catch (NoCandidatesAvailableException) { return (request, EInstallationMatchingOutcome.NoCandidates); }
        return (request, EInstallationMatchingOutcome.Assigned);
    }
}
