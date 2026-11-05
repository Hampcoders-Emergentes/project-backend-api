using Hampcoders.Electrolink.API.Processing.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Processing.Domain.Repositories;
using Hampcoders.Electrolink.API.Processing.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Application.Authorization;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using ProfilesAcl = Hampcoders.Electrolink.API.Profiles.Interfaces.ACL.IProfilesContextFacade;

namespace Hampcoders.Electrolink.API.Processing.Application.Internal.QueryServices;

public class DeviceAccessService(
    ProfilesAcl profiles,
    IDeviceReadingStreamRepository streamRepository,
    IAnomalyRecordRepository anomalyRepository,
    IServiceOperationContextFacade serviceOperations) : IDeviceAccessService
{
    public async Task<bool> CanAccessDeviceAsync(string userId, string deviceId)
    {
        var stream = await streamRepository.FindByDeviceIdAsync(deviceId);
        return stream is not null && await CanAccessAsync(userId, stream.Owner, stream.PropertyId.Value);
    }

    public async Task<bool> CanAccessAnomalyAsync(string userId, string anomalyId)
    {
        var anomaly = await anomalyRepository.FindByIdAsync(AnomalyRecordId.From(anomalyId));
        return anomaly is not null && await CanAccessAsync(userId, anomaly.Owner, anomaly.PropertyId.Value);
    }

    public async Task<IReadOnlyList<AnomalyRecord>> FilterVisibleAnomaliesAsync(
        string userId, string propertyId, IEnumerable<AnomalyRecord> anomalies)
    {
        var subject = await ActorSubjectResolver.ResolveAsync(profiles, userId);
        if (subject is null) return [];
        if (await HasActiveServiceAsync(subject, propertyId)) return anomalies.ToList();
        return anomalies.Where(anomaly => subject.IsClient(anomaly.Owner)).ToList();
    }

    private async Task<bool> CanAccessAsync(string userId, ClientIdentity owner, string propertyId)
    {
        var subject = await ActorSubjectResolver.ResolveAsync(profiles, userId);
        return subject is not null && (subject.IsClient(owner) || await HasActiveServiceAsync(subject, propertyId));
    }

    private async Task<bool> HasActiveServiceAsync(ActorSubject subject, string propertyId)
        => subject.Technician is not null
           && await serviceOperations.HasActiveServiceForPropertyAsync(propertyId, subject.Technician.Value);
}
