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

namespace Electrolink.Backend.UnitTests.Processing.Interfaces.REST.Transform;

public class ProcessingAssemblersTests
{
    [Fact]
    public void AnomalyResource_ToResourceFromEntity_MapsAnomaly()
    {
        // Arrange
        var anomaly = Anomaly();

        // Act
        var resource = AnomalyResourceFromEntityAssembler.ToResourceFromEntity(anomaly);

        // Assert
        Assert.Equal((anomaly.AnomalyId.Value, "Overcurrent", "High", "Active", false),
            (resource.AnomalyId, resource.AnomalyType, resource.Severity, resource.Status, resource.AutoRelayActivated));
    }

    [Fact]
    public void DeviceStatusResource_ToResourceFromEntityWithoutReadings_HasNoLastReading()
    {
        // Arrange
        var stream = Stream();

        // Act
        var resource = DeviceStatusResourceFromEntityAssembler.ToResourceFromEntity(stream, [], "UNKNOWN");

        // Assert
        Assert.Equal(("CONNECTED", (Hampcoders.Electrolink.API.Processing.Interfaces.REST.Resources.ReadingValueResource?)null, "UNKNOWN"),
            (resource.ConnectionStatus, resource.LastReading, resource.CurrentRelayState));
    }

    [Fact]
    public void DeviceStatusResource_ToResourceFromPausedStream_ReportsDisconnected()
    {
        // Arrange
        var stream = Stream();
        stream.Pause();

        // Act
        var resource = DeviceStatusResourceFromEntityAssembler.ToResourceFromEntity(stream, [], "Open");

        // Assert
        Assert.Equal(("DISCONNECTED", "Inactive"), (resource.ConnectionStatus, resource.StreamStatus));
    }

    [Fact]
    public void DeviceStatusResource_ToResourceFromEntityWithReadings_UsesMostRecentReading()
    {
        // Arrange
        var stream = Stream();
        stream.IngestReading(NormalReading(stream, "rd-old", DateTime.UtcNow.AddMinutes(-10)));
        stream.IngestReading(Hampcoders.Electrolink.API.Processing.Domain.Model.Entities.Reading.Create(ReadingId.From("rd-new"), stream.StreamId,
            DateTime.UtcNow, 230, 6, 0.9f, 60, EReadingSource.EdgeDevice));

        // Act
        var resource = DeviceStatusResourceFromEntityAssembler.ToResourceFromEntity(stream, [Anomaly()], "Closed");

        // Assert
        Assert.Equal((230f, 1), (resource.LastReading!.Voltage, resource.ActiveAnomalies.Count));
    }

    [Fact]
    public void RelayHistory_ToResourceFromEntitiesWithoutCommands_ReportsUnknownState()
    {
        // Act
        var resource = RelayHistoryResourceFromEntityAssembler.ToResourceFromEntities(Device, []);

        // Assert
        Assert.Equal(("UNKNOWN", (DateTime?)null, 0), (resource.CurrentRelayState, resource.LastCommandAt, resource.Commands.Count));
    }

    [Fact]
    public void RelayHistory_ToResourceFromEntitiesWithExecutedCommand_ReportsTargetState()
    {
        // Arrange
        var command = SentRelayCommand();
        command.MarkAsExecuted();

        // Act
        var resource = RelayHistoryResourceFromEntityAssembler.ToResourceFromEntities(Device, [command]);

        // Assert
        Assert.Equal(("Open", "Executed"), (resource.CurrentRelayState, resource.Commands.Single().Status));
    }

    [Fact]
    public void RelayHistory_ToResourceFromEntitiesWithPendingCommand_ReportsUnknownState()
    {
        // Act
        var resource = RelayHistoryResourceFromEntityAssembler.ToResourceFromEntities(Device, [SentRelayCommand()]);

        // Assert
        Assert.Equal("UNKNOWN", resource.CurrentRelayState);
    }
}
