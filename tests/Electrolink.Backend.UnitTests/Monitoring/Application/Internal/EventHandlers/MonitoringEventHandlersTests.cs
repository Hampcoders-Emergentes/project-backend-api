using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Assets.Interfaces.ACL;
using Hampcoders.Electrolink.API.Monitoring.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Monitoring.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Monitoring.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Events;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Monitoring.Domain.Repositories;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Events;
using Hampcoders.Electrolink.API.Processing.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using static Electrolink.Backend.UnitTests.Monitoring.MonitoringTestData;
using ServiceCompletedEventHandler = Hampcoders.Electrolink.API.Monitoring.Application.Internal.EventHandlers.ServiceCompletedEventHandler;

namespace Electrolink.Backend.UnitTests.Monitoring.Application.Internal.EventHandlers;

public class MonitoringLoggingEventHandlersTests
{
    [Fact]
    public async Task AnomalyDetectedHandle_WithEvent_CompletesSuccessfully()
    {
        // Arrange
        var sut = new AnomalyDetectedEventHandler(NullLogger<AnomalyDetectedEventHandler>.Instance);
        var evt = new ActiveServiceAnomalyAlertedEvent(ServiceExecutionId.NewId(), DeviceId.From("dev-1"), "OVERCURRENT", "spike", DateTime.UtcNow);

        // Act
        var task = sut.Handle(evt, CancellationToken.None);

        // Assert
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task CircuitToggleRequestedHandle_WithEvent_CompletesSuccessfully()
    {
        // Arrange
        var execution = InProgress(Iot());
        execution.RemotelyToggleCircuit(Technician, ERelayState.Off, Ids.User);
        var sut = new CircuitToggleRequestedEventHandler(NullLogger<CircuitToggleRequestedEventHandler>.Instance);

        // Act
        var task = sut.Handle(execution.DomainEvents.OfType<CircuitToggleRequestedEvent>().Single(), CancellationToken.None);

        // Assert
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task CircuitToggleRecordedHandle_WithEvent_CompletesSuccessfully()
    {
        // Arrange
        var execution = InProgress(Iot());
        execution.RemotelyToggleCircuit(Technician, ERelayState.Off, Ids.User);
        execution.RecordCircuitToggle(DeviceId.From("dev-1"), ERelayState.Off, ERelayActionStatus.Failed, "timeout");
        var sut = new CircuitToggleRecordedEventHandler(NullLogger<CircuitToggleRecordedEventHandler>.Instance);

        // Act
        var task = sut.Handle(execution.DomainEvents.OfType<CircuitToggleRecordedEvent>().Single(), CancellationToken.None);

        // Assert
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ClientReviewSubmittedHandle_WithEvent_CompletesSuccessfully()
    {
        // Arrange
        var sut = new ClientReviewSubmittedEventHandler(NullLogger<ClientReviewSubmittedEventHandler>.Instance);
        var evt = new ClientReviewSubmittedEvent(ServiceExecutionId.NewId(), AssignmentId.NewAssignmentId(), Homeowner, Ids.Technician, 5, [],
            DateTime.UtcNow, DateTime.UtcNow);

        // Act
        var task = sut.Handle(evt, CancellationToken.None);

        // Assert
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ServiceExecutionCreatedHandle_WithEvent_CompletesSuccessfully()
    {
        // Arrange
        var sut = new ServiceExecutionCreatedEventHandler(NullLogger<ServiceExecutionCreatedEventHandler>.Instance);
        var evt = new ServiceExecutionCreatedEvent(ServiceExecutionId.NewId(), AssignmentId.NewAssignmentId(), Technician, Owner, DateTime.UtcNow);

        // Act
        var task = sut.Handle(evt, CancellationToken.None);

        // Assert
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task TechnicianReviewSubmittedHandle_WithEvent_CompletesSuccessfully()
    {
        // Arrange
        var sut = new TechnicianReviewSubmittedEventHandler(NullLogger<TechnicianReviewSubmittedEventHandler>.Instance);
        var evt = new TechnicianReviewSubmittedEvent(ServiceExecutionId.NewId(), AssignmentId.NewAssignmentId(), Ids.Technician, Homeowner, 4,
            [], DateTime.UtcNow, DateTime.UtcNow);

        // Act
        var task = sut.Handle(evt, CancellationToken.None);

        // Assert
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }
}

public class MonitoringForwardingEventHandlersTests
{
    [Fact]
    public async Task RelayCommandExecutedHandle_WithEvent_PublishesCircuitToggledRemotely()
    {
        // Arrange
        var mediator = new Fake<IMediator>();
        var property = PropertyId.NewPropertyId();
        var sut = new RelayCommandExecutedEventHandler(mediator.Object, NullLogger<RelayCommandExecutedEventHandler>.Instance);

        // Act
        await sut.Handle(new RelayCommandExecutedEvent("cmd-1", "dev-1", property.Value, "off", Ids.User, null, DateTime.UtcNow),
            CancellationToken.None);

        // Assert
        var published = mediator.Calls.Single().Arg<CircuitToggledRemotelyEvent>();
        Assert.Equal((DeviceId.From("dev-1"), property, ERelayState.Off), (published.DeviceId, published.PropertyId, published.TargetState));
    }

    [Fact]
    public async Task RelayCommandExecutedHandle_WithUnknownRelayState_ThrowsArgumentException()
    {
        // Arrange
        var sut = new RelayCommandExecutedEventHandler(new Fake<IMediator>().Object, NullLogger<RelayCommandExecutedEventHandler>.Instance);

        // Act
        var act = () => sut.Handle(new RelayCommandExecutedEvent("cmd-1", "dev-1", PropertyId.NewPropertyId().Value, "dimmed", Ids.User, null,
            DateTime.UtcNow), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task ServiceCompletedHandle_WithEvent_NotifiesAnalyticsOfCompletion()
    {
        // Arrange
        var analytics = new Fake<IExternalAnalyticsService>();
        var execution = ReadyToComplete();
        var completedAt = DateTime.UtcNow;
        execution.Complete(Technician, completedAt);
        var sut = new ServiceCompletedEventHandler(analytics.Object, NullLogger<ServiceCompletedEventHandler>.Instance);

        // Act
        await sut.Handle(execution.DomainEvents.OfType<ServiceCompletedEvent>().Single(), CancellationToken.None);

        // Assert
        Assert.Equal(new object?[] { execution.Id.Value, Ids.Technician, completedAt }, analytics.Calls.Single().Args);
    }

    [Fact]
    public async Task ServiceExecutionCancelledHandle_WithEvent_NotifiesAnalyticsOfCancellation()
    {
        // Arrange
        var analytics = new Fake<IExternalAnalyticsService>();
        var execution = Notified();
        execution.Cancel(null, Homeowner, ECancelledBy.Homeowner, ECancellationReason.ScheduleConflict, null, false);
        var sut = new ServiceExecutionCancelledEventHandler(analytics.Object, NullLogger<ServiceExecutionCancelledEventHandler>.Instance);

        // Act
        await sut.Handle(execution.DomainEvents.OfType<ServiceExecutionCancelledEvent>().Single(), CancellationToken.None);

        // Assert
        var call = analytics.CallsTo(nameof(IExternalAnalyticsService.NotifyCancellationAsync)).Single();
        Assert.Equal((execution.Id.Value, "ScheduleConflict"), (call.Arg<string>(0), call.Arg<string>(1)));
    }
}
