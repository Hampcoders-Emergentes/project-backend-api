using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Events;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Processing.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Processing.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Processing.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Processing.Domain.Repositories;
using Hampcoders.Electrolink.API.Processing.Domain.Services;
using Hampcoders.Electrolink.API.Processing.Interfaces.ACL.Services;
using Hampcoders.Electrolink.API.Processing.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using static Electrolink.Backend.UnitTests.Processing.ProcessingTestData;
using IAssetsPort = Hampcoders.Electrolink.API.Processing.Application.Internal.OutboundServices.IAssetsContextFacade;
using ProfilesAcl = Hampcoders.Electrolink.API.Profiles.Interfaces.ACL.IProfilesContextFacade;

namespace Electrolink.Backend.UnitTests.Processing.Application.Internal.QueryServices;

public class IoTMonitoringQueryServiceTests
{
    private readonly Fake<IDeviceReadingStreamRepository> _streams = new();
    private readonly Fake<IAnomalyRecordRepository> _anomalies = new();
    private readonly Fake<IRelayControlCommandRepository> _relays = new();

    private IoTMonitoringQueryService CreateSut() => new(_streams.Object, _anomalies.Object, _relays.Object);

    [Fact]
    public async Task Handle_GetDeviceStatus_ReturnsDeviceStream()
    {
        // Arrange
        var stream = Stream();
        _streams.Returns(nameof(IDeviceReadingStreamRepository.FindByDeviceIdAsync), stream);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetDeviceStatusQuery(Device));

        // Assert
        Assert.Same(stream, result);
    }

    [Fact]
    public async Task Handle_GetRecentReadingsWindow_ReturnsDeviceStream()
    {
        // Arrange
        var stream = Stream();
        _streams.Returns(nameof(IDeviceReadingStreamRepository.FindByDeviceIdAsync), stream);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetRecentReadingsWindowQuery(Device));

        // Assert
        Assert.Same(stream, result);
    }

    [Fact]
    public async Task Handle_GetActiveAnomalies_QueriesByProperty()
    {
        // Arrange
        _anomalies.Returns(nameof(IAnomalyRecordRepository.FindActiveByPropertyAsync), new[] { Anomaly() });
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetActiveAnomaliesByPropertyQuery(Property.Value));

        // Assert
        Assert.Single(result);
        Assert.Equal(Property.Value, _anomalies.Calls.Single().Arg<string>());
    }

    [Fact]
    public async Task Handle_GetRelayHistory_ReturnsDeviceHistory()
    {
        // Arrange
        _relays.Returns(nameof(IRelayControlCommandRepository.FindHistoryByDeviceAsync), new[] { SentRelayCommand() });
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetRelayCommandHistoryQuery(Device));

        // Assert
        Assert.Single(result);
    }

    [Fact]
    public async Task FindPendingRelayCommandByDeviceAsync_WithoutPendingCommand_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.FindPendingRelayCommandByDeviceAsync(Device);

        // Assert
        Assert.Null(result);
    }
}
