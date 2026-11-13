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

namespace Electrolink.Backend.UnitTests.Monitoring.Application.Internal.OutboundServices;

public class MonitoringOutboundServicesTests
{
    [Fact]
    public async Task ExternalAnalyticsService_NotifyCompletionAsync_PublishesServiceCompletedIntegrationEvent()
    {
        // Arrange
        var mediator = new Fake<IMediator>();
        var completedAt = DateTime.UtcNow;
        var sut = new ExternalAnalyticsService(mediator.Object, NullLogger<ExternalAnalyticsService>.Instance);

        // Act
        await sut.NotifyCompletionAsync("exec-1", Ids.Technician, completedAt);

        // Assert
        var evt = mediator.Calls.Single().Arg<ServiceCompletedIntegrationEvent>();
        Assert.Equal(("exec-1", Ids.Technician, completedAt), (evt.ServiceId, evt.TechnicianId, evt.CompletedAt));
    }

    [Fact]
    public async Task ExternalAnalyticsService_NotifyCompletionAsyncWhenPublishFails_SwallowsException()
    {
        // Arrange
        var mediator = new Fake<IMediator>().Throws(nameof(IMediator.Publish), new InvalidOperationException("bus down"));
        var sut = new ExternalAnalyticsService(mediator.Object, NullLogger<ExternalAnalyticsService>.Instance);

        // Act
        var exception = await Record.ExceptionAsync(() => sut.NotifyCompletionAsync("exec-1", Ids.Technician, DateTime.UtcNow));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task ExternalAnalyticsService_NotifyCancellationAsync_DoesNotPublish()
    {
        // Arrange
        var mediator = new Fake<IMediator>();
        var sut = new ExternalAnalyticsService(mediator.Object, NullLogger<ExternalAnalyticsService>.Instance);

        // Act
        await sut.NotifyCancellationAsync("exec-1", "Other", DateTime.UtcNow);

        // Assert
        Assert.Empty(mediator.Calls);
    }

    [Fact]
    public async Task ExternalAssetsService_GetDeviceBySerialNumberAsync_WhenFound_ReturnsDeviceId()
    {
        // Arrange
        var assets = new Fake<IAssetsContextFacade>().Returns(nameof(IAssetsContextFacade.GetDeviceIdBySerialNumberAsync), "dev-9");
        var sut = new ExternalAssetsService(assets.Object, NullLogger<ExternalAssetsService>.Instance);

        // Act
        var result = await sut.GetDeviceBySerialNumberAsync("SN-9");

        // Assert
        Assert.Equal(DeviceId.From("dev-9"), result);
    }

    [Fact]
    public async Task ExternalAssetsService_GetDeviceBySerialNumberAsync_WhenMissing_ReturnsNull()
    {
        // Arrange
        var sut = new ExternalAssetsService(new Fake<IAssetsContextFacade>().Object, NullLogger<ExternalAssetsService>.Instance);

        // Act
        var result = await sut.GetDeviceBySerialNumberAsync("SN-9");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ExternalAssetsService_GetDeviceBySerialNumberAsync_WithMalformedDeviceId_ReturnsNull()
    {
        // Arrange
        var assets = new Fake<IAssetsContextFacade>().Returns(nameof(IAssetsContextFacade.GetDeviceIdBySerialNumberAsync), "device-9");
        var sut = new ExternalAssetsService(assets.Object, NullLogger<ExternalAssetsService>.Instance);

        // Act
        var result = await sut.GetDeviceBySerialNumberAsync("SN-9");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ExternalIoTService_IssueRelayCommandAsync_RequestsToggleOnBehalfOfIssuer()
    {
        // Arrange
        var processing = new Fake<IProcessingContextFacade>();
        var sut = new ExternalIoTService(processing.Object, NullLogger<ExternalIoTService>.Instance);

        // Act
        await sut.IssueRelayCommandAsync("exec-1", "dev-1", "Off", Ids.User);

        // Assert
        var args = processing.Calls.Single().Args;
        Assert.Equal(("exec-1", "dev-1", Ids.User, "Off"), (args[0], args[1], args[2], args[3]));
    }

    [Fact]
    public async Task ExternalIoTService_IssueRelayCommandAsyncWhenFacadeFails_SwallowsException()
    {
        // Arrange
        var processing = new Fake<IProcessingContextFacade>().Throws(nameof(IProcessingContextFacade.RequestRelayToggleAsync),
            new InvalidOperationException("offline"));
        var sut = new ExternalIoTService(processing.Object, NullLogger<ExternalIoTService>.Instance);

        // Act
        var exception = await Record.ExceptionAsync(() => sut.IssueRelayCommandAsync("exec-1", "dev-1", "Off", Ids.User));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task ExternalMonitoringService_PublishCircuitToggleRequestAsync_RequestsToggleAsSystem()
    {
        // Arrange
        var processing = new Fake<IProcessingContextFacade>();
        var sut = new ExternalMonitoringService(processing.Object, NullLogger<ExternalMonitoringService>.Instance);

        // Act
        await sut.PublishCircuitToggleRequestAsync("exec-1", "dev-1", "On");

        // Assert
        var args = processing.Calls.Single().Args;
        Assert.Equal(("SYSTEM", "On"), (args[2], args[3]));
    }

    [Fact]
    public async Task ExternalMonitoringService_PublishCircuitToggleRequestAsyncWhenFacadeFails_SwallowsException()
    {
        // Arrange
        var processing = new Fake<IProcessingContextFacade>().Throws(nameof(IProcessingContextFacade.RequestRelayToggleAsync),
            new InvalidOperationException("offline"));
        var sut = new ExternalMonitoringService(processing.Object, NullLogger<ExternalMonitoringService>.Instance);

        // Act
        var exception = await Record.ExceptionAsync(() => sut.PublishCircuitToggleRequestAsync("exec-1", "dev-1", "On"));

        // Assert
        Assert.Null(exception);
    }
}
