using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Assets.Interfaces.ACL;
using Hampcoders.Electrolink.API.Monitoring.Interfaces.ACL;
using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Processing.Interfaces.ACL;
using Hampcoders.Electrolink.API.Processing.Interfaces.ACL.Services;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.ACL;
using Microsoft.Extensions.Logging.Abstractions;
using static Electrolink.Backend.UnitTests.Planning.PlanningTestData;

namespace Electrolink.Backend.UnitTests.Planning.Application.Internal.OutboundServices;

public class ContextSnapshotServiceTests
{
    [Fact]
    public async Task CaptureAsync_WithoutDevice_ReturnsNull()
    {
        // Arrange
        var sut = new ContextSnapshotService(new Fake<IProcessingContextFacade>().Object);

        // Act
        var result = await sut.CaptureAsync("prop-1");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CaptureAsync_WithDeviceTelemetry_MapsReadingsAndAnomalies()
    {
        // Arrange
        var at = DateTime.UtcNow;
        var dto = new IoTContextSnapshotDto("dev-1", "INSTALLED", "LIVE", at, [new ReadingEntryDto(at, 220f, 5f, 0.5f, 60f)],
            [new AnomalyEntryDto("an-1", "SPIKE", "HIGH", at)]);
        var processing = new Fake<IProcessingContextFacade>().Returns(nameof(IProcessingContextFacade.GetIoTContextSnapshotAsync), dto);
        var sut = new ContextSnapshotService(processing.Object);

        // Act
        var result = await sut.CaptureAsync("prop-1");

        // Assert
        Assert.Equal(("dev-1", 220m, 0.5m, "SPIKE"), (result!.DeviceId, result.LatestReadings.Single().Voltage,
            result.LatestReadings.Single().PowerFactor, result.ActiveAnomalies.Single().AnomalyType));
    }
}
