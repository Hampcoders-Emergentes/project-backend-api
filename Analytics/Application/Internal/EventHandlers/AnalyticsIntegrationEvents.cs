using Hampcoders.Electrolink.API.Shared.Domain.Model.Events;

namespace Hampcoders.Electrolink.API.Analytics.Application.Internal.EventHandlers;

public record ServiceCompletedIntegrationEvent(
    string OwnerId,
    string ServiceId,
    string ServiceType,
    DateTime CompletedAt,
    string TechnicianId,
    decimal ServiceRevenue,
    string Currency,
    TimeSpan ResponseTime,
    bool RequiresIoTCertification) : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = CompletedAt;
}

public record TechnicianEvaluationSubmittedIntegrationEvent(
    string TechnicianId,
    int Score,
    string Feedback,
    DateTime SubmittedAt) : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = SubmittedAt;
}

public record ConsumptionThresholdsUpdatedIntegrationEvent(
    string OwnerId,
    Dictionary<string, decimal> Thresholds,
    DateTime UpdatedAt) : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = UpdatedAt;
}

