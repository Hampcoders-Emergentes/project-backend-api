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

namespace Electrolink.Backend.UnitTests.Processing.Interfaces.ACL.Services;

public class ProcessingContextFacadeTests
{
    private readonly Fake<IRelayCommandService> _relays = new();
    private readonly Fake<IDeviceReadingStreamRepository> _streams = new();
    private readonly Fake<IAnomalyRecordRepository> _anomalies = new();
    private readonly Fake<IServiceOperationContextFacade> _services = new();

    private ProcessingContextFacade CreateSut() => new(_relays.Object, new Fake<IIoTMonitoringQueryService>().Object, _streams.Object,
        _anomalies.Object, _services.Object);

    [Fact]
    public async Task RequestRelayToggleAsync_ForUnknownDevice_ThrowsKeyNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.RequestRelayToggleAsync("exec-1", Device, Ids.Technician, "Open", "maintenance", DateTime.UtcNow);

        // Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(act);
    }

    [Fact]
    public async Task RequestRelayToggleAsync_ForKnownDevice_IssuesRelayCommandForStreamProperty()
    {
        // Arrange
        _streams.Returns(nameof(IDeviceReadingStreamRepository.FindByDeviceIdAsync), Stream());
        var sut = CreateSut();

        // Act
        await sut.RequestRelayToggleAsync("exec-1", Device, Ids.Technician, "Closed", "maintenance", DateTime.UtcNow);

        // Assert
        Assert.Equal(new IssueRelayCommandCommand(Device, Property.Value, Ids.Technician, "exec-1", "Closed"),
            _relays.Calls.Single().Arg<IssueRelayCommandCommand>());
    }

    [Fact]
    public async Task GetIoTContextSnapshotAsync_WithoutStream_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.GetIoTContextSnapshotAsync(Property.Value);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetIoTContextSnapshotAsync_WithStream_ReturnsLatestTenReadingsAndActiveAnomalies()
    {
        // Arrange
        var stream = Stream();
        for (var i = 0; i < 12; i++) stream.IngestReading(NormalReading(stream, $"rd-{i}", DateTime.UtcNow.AddMinutes(-i)));
        _streams.Returns(nameof(IDeviceReadingStreamRepository.FindByPropertyIdAsync), stream);
        _anomalies.Returns(nameof(IAnomalyRecordRepository.FindActiveByPropertyAsync), new[] { Anomaly() });
        var sut = CreateSut();

        // Act
        var result = await sut.GetIoTContextSnapshotAsync(Property.Value);

        // Assert
        Assert.Equal((Device, 10, "Overcurrent"), (result!.DeviceId, result.RecentReadings.Count, result.ActiveAnomalies.Single().AnomalyType));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HasActiveServiceForPropertyAsync_ReturnsServiceOperationResult(bool active)
    {
        // Arrange
        _services.Returns(nameof(IServiceOperationContextFacade.HasActiveServiceForPropertyAsync), active);
        var sut = CreateSut();

        // Act
        var result = await sut.HasActiveServiceForPropertyAsync(Property.Value, Ids.Technician);

        // Assert
        Assert.Equal(active, result);
    }
}
