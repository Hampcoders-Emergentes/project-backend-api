using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Analytics.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Electrolink.Backend.UnitTests.Analytics.Application.Internal.EventHandlers;

public class ServiceCompletedEventHandlerTests
{
    [Fact]
    public async Task Handle_WithCompletedService_UpdatesTechnicianMetricsWithEventData()
    {
        // Arrange
        var metrics = new Fake<ITechnicianMetricsCommandService>();
        var sut = new ServiceCompletedEventHandler(metrics.Object, NullLogger<ServiceCompletedEventHandler>.Instance);
        var notification = new ServiceCompletedIntegrationEvent("owner-1", "svc-1", "INSTALL", DateTime.UtcNow, Ids.Technician, 120m, "PEN",
            TimeSpan.FromHours(3), true);

        // Act
        await sut.Handle(notification, CancellationToken.None);

        // Assert
        var call = Assert.Single(metrics.CallsTo(nameof(ITechnicianMetricsCommandService.UpdateTechnicianMetricsAsync)));
        Assert.Equal([Ids.Technician, "owner-1", 120m, "PEN", TimeSpan.FromHours(3), true], call.Args);
    }
}

public class TechnicianEvaluationSubmittedEventHandlerTests
{
    [Fact]
    public async Task Handle_WithEvaluation_UpdatesRatingWithScore()
    {
        // Arrange
        var metrics = new Fake<ITechnicianMetricsCommandService>();
        var sut = new TechnicianEvaluationSubmittedEventHandler(metrics.Object, NullLogger<TechnicianEvaluationSubmittedEventHandler>.Instance);

        // Act
        await sut.Handle(new TechnicianEvaluationSubmittedIntegrationEvent(Ids.Technician, 5, "great", DateTime.UtcNow), CancellationToken.None);

        // Assert
        var call = Assert.Single(metrics.CallsTo(nameof(ITechnicianMetricsCommandService.UpdateTechnicianRatingMetricAsync)));
        Assert.Equal([Ids.Technician, 5m], call.Args);
    }
}

public class AnalyticsIntegrationEventsTests
{
    [Fact]
    public void ServiceCompletedIntegrationEvent_WhenCreated_UsesCompletionTimeAsOccurredOn()
    {
        // Arrange
        var completedAt = new DateTime(2025, 5, 1, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var evt = new ServiceCompletedIntegrationEvent("o", "s", "t", completedAt, Ids.Technician, 1, "PEN", TimeSpan.Zero, false);

        // Assert
        Assert.Equal(completedAt, evt.OccurredOn);
    }

    [Fact]
    public void TechnicianEvaluationSubmittedIntegrationEvent_WhenCreatedTwice_AssignsDistinctEventIds()
    {
        // Arrange
        var at = DateTime.UtcNow;

        // Act
        var first = new TechnicianEvaluationSubmittedIntegrationEvent(Ids.Technician, 4, "", at);
        var second = new TechnicianEvaluationSubmittedIntegrationEvent(Ids.Technician, 4, "", at);

        // Assert
        Assert.NotEqual(first.EventId, second.EventId);
    }

    [Fact]
    public void ConsumptionThresholdsUpdatedIntegrationEvent_WhenCreated_UsesUpdateTimeAsOccurredOn()
    {
        // Arrange
        var updatedAt = new DateTime(2025, 5, 1, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var evt = new ConsumptionThresholdsUpdatedIntegrationEvent("o", new Dictionary<string, decimal> { ["max"] = 5 }, updatedAt);

        // Assert
        Assert.Equal(updatedAt, evt.OccurredOn);
    }
}
