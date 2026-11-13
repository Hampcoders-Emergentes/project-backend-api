using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.AlertManagement.Application;
using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.Processing.Application.Internal.CommandHandlers;
using Hampcoders.Electrolink.API.Processing.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Processing.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Events;
using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Processing.Domain.Repositories;
using Hampcoders.Electrolink.API.Processing.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using static Electrolink.Backend.UnitTests.Processing.ProcessingTestData;

namespace Electrolink.Backend.UnitTests.Processing.Application.Internal.CommandServices;

public class MqttIngestionCommandHandlerTests
{
    private readonly Fake<IDeviceReadingStreamCommandService> _streams = new();
    private readonly Fake<IRelayCommandService> _relays = new();

    private MqttIngestionCommandHandler CreateSut() => new(_streams.Object, _relays.Object);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_IngestReading_ReturnsStreamServiceResult(bool accepted)
    {
        // Arrange
        _streams.Returns(nameof(IDeviceReadingStreamCommandService.Handle), accepted);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new IngestDeviceReadingCommand(Device, "rd-1", DateTime.UtcNow, 220, 5, 0.9f, 60, "EdgeDevice"),
            CancellationToken.None);

        // Assert
        Assert.Equal(accepted, result);
    }

    [Fact]
    public async Task Handle_ReportEdgeAnomaly_DelegatesToStreamService()
    {
        // Arrange
        var command = new ReportEdgeAnomalyCommand(Device, "Overcurrent", "rd-1", "{}");
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(Unit.Value, result);
        Assert.Same(command, _streams.Calls.Single().Args[0]);
    }

    [Fact]
    public async Task Handle_MarkDisconnected_DelegatesToStreamService()
    {
        // Arrange
        var command = new MarkDeviceAsDisconnectedCommand(Device);
        var sut = CreateSut();

        // Act
        await sut.Handle(command, CancellationToken.None);

        // Assert
        Assert.Same(command, _streams.Calls.Single().Args[0]);
    }

    [Fact]
    public async Task Handle_AcknowledgeRelayExecution_DelegatesToRelayService()
    {
        // Arrange
        var command = new AcknowledgeRelayExecutionCommand("relay-1", Device, true, DateTime.UtcNow);
        var sut = CreateSut();

        // Act
        await sut.Handle(command, CancellationToken.None);

        // Assert
        Assert.Same(command, _relays.Calls.Single().Args[0]);
    }
}

public class AnomalyCommandServiceTests
{
    private readonly Fake<IDeviceReadingStreamRepository> _streams = new();
    private readonly Fake<IAnomalyRecordRepository> _anomalies = new();
    private readonly Fake<IRelayCommandService> _relays = new();
    private readonly Fake<IMediator> _mediator = new();

    private AnomalyCommandService CreateSut() => new(_streams.Object, _anomalies.Object, _relays.Object, new Fake<IUnitOfWork>().Object,
        _mediator.Object, NullLogger<AnomalyCommandService>.Instance);

    private DeviceReadingStream Given(DeviceReadingStream stream)
    {
        _streams.Returns(nameof(IDeviceReadingStreamRepository.FindByStreamIdAsync), stream);
        return stream;
    }

    private AnomalyRecord GivenAnomaly(AnomalyRecord anomaly)
    {
        _anomalies.Returns(nameof(IAnomalyRecordRepository.FindByIdAsync), anomaly);
        return anomaly;
    }

    [Fact]
    public async Task Handle_EvaluateWhenStreamMissing_RecordsNothing()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(new EvaluateReadingForAnomaliesCommand("stream-1", "rd-1"));

        // Assert
        Assert.Empty(_anomalies.Calls);
    }

    [Fact]
    public async Task Handle_EvaluateOvercurrentReading_RecordsHighOvercurrentAnomaly()
    {
        // Arrange
        var stream = Given(StreamWithReading("rd-1", current: 25));
        var sut = CreateSut();

        // Act
        await sut.Handle(new EvaluateReadingForAnomaliesCommand(stream.StreamId.Value, "rd-1"));

        // Assert
        var anomaly = _anomalies.CallsTo(nameof(IAnomalyRecordRepository.AddAsync)).Single().Arg<AnomalyRecord>();
        Assert.Equal((EAnomalyType.Overcurrent, EAnomalySeverity.High), (anomaly.AnomalyType, anomaly.Severity));
        Assert.Empty(_relays.Calls);
    }

    [Fact]
    public async Task Handle_EvaluateWhenSameAnomalyIsActive_SupersedesPreviousAnomaly()
    {
        // Arrange
        var stream = Given(StreamWithReading("rd-1", current: 25));
        var previous = Anomaly();
        _anomalies.Returns(nameof(IAnomalyRecordRepository.FindActiveByDeviceAndTypeAsync), previous);
        var sut = CreateSut();

        // Act
        await sut.Handle(new EvaluateReadingForAnomaliesCommand(stream.StreamId.Value, "rd-1"));

        // Assert
        Assert.Equal(EAnomalyStatus.Superseded, previous.Status);
    }

    [Fact]
    public async Task Handle_EvaluateShortCircuitRisk_OpensRelayAutomatically()
    {
        // Arrange
        var stream = Given(StreamWithReading("rd-1", current: 25, powerFactor: 0.5f));
        var sut = CreateSut();

        // Act
        await sut.Handle(new EvaluateReadingForAnomaliesCommand(stream.StreamId.Value, "rd-1"));

        // Assert
        var relay = _relays.Calls.Single().Arg<IssueRelayCommandCommand>();
        Assert.Equal(("SYSTEM", "Open", Device), (relay.TechnicianId, relay.TargetRelayState, relay.DeviceId));
    }

    [Fact]
    public async Task Handle_EvaluateNormalReading_RegistersNormalReadingWithoutAnomaly()
    {
        // Arrange
        var stream = Given(StreamWithReading("rd-1"));
        var sut = CreateSut();

        // Act
        await sut.Handle(new EvaluateReadingForAnomaliesCommand(stream.StreamId.Value, "rd-1"));

        // Assert
        Assert.Equal(1, stream.ConsecutiveNormalCount);
        Assert.Equal(0, _anomalies.CallCount(nameof(IAnomalyRecordRepository.AddAsync)));
    }

    [Fact]
    public async Task Handle_EvaluateAfterEnoughNormalReadings_AutoResolvesNonCriticalAnomalies()
    {
        // Arrange
        var stream = Given(StreamWithReading("rd-1"));
        for (var i = 0; i < 4; i++) stream.RegisterNormalReading();
        var minor = Anomaly();
        var critical = Anomaly(EAnomalyType.ShortCircuitRisk, EAnomalySeverity.Critical);
        _anomalies.Returns(nameof(IAnomalyRecordRepository.FindActiveByPropertyAsync), new[] { minor, critical });
        var sut = CreateSut();

        // Act
        await sut.Handle(new EvaluateReadingForAnomaliesCommand(stream.StreamId.Value, "rd-1"));

        // Assert
        Assert.Equal((EAnomalyStatus.Resolved, EAnomalyStatus.Active), (minor.Status, critical.Status));
    }

    [Fact]
    public async Task Handle_AcknowledgeMissingAnomaly_ThrowsKeyNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new AcknowledgeAnomalyCommand("anm-1", Ids.User));

        // Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_AcknowledgeActiveAnomaly_MarksAcknowledged()
    {
        // Arrange
        var anomaly = GivenAnomaly(Anomaly());
        var sut = CreateSut();

        // Act
        await sut.Handle(new AcknowledgeAnomalyCommand(anomaly.AnomalyId.Value, Ids.User));

        // Assert
        Assert.Equal(EAnomalyStatus.Acknowledged, anomaly.Status);
    }

    [Fact]
    public async Task Handle_ForceResolveAcknowledgedAnomaly_MarksResolved()
    {
        // Arrange
        var anomaly = GivenAnomaly(Anomaly(EAnomalyType.ShortCircuitRisk, EAnomalySeverity.Critical));
        anomaly.Acknowledge();
        var sut = CreateSut();

        // Act
        await sut.Handle(new ForceResolveAnomalyCommand(anomaly.AnomalyId.Value, Ids.User));

        // Assert
        Assert.Equal(EAnomalyStatus.Resolved, anomaly.Status);
    }

    [Fact]
    public async Task Handle_ForceResolveMissingAnomaly_ThrowsKeyNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new ForceResolveAnomalyCommand("anm-1", Ids.User));

        // Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_ResolveBelowNormalThreshold_LeavesAnomalyActive()
    {
        // Arrange
        var anomaly = GivenAnomaly(Anomaly());
        var sut = CreateSut();

        // Act
        await sut.Handle(new ResolveAnomalyCommand(anomaly.AnomalyId.Value, 4));

        // Assert
        Assert.Equal(EAnomalyStatus.Active, anomaly.Status);
    }

    [Fact]
    public async Task Handle_ResolveCriticalAnomaly_LeavesAnomalyActive()
    {
        // Arrange
        var anomaly = GivenAnomaly(Anomaly(EAnomalyType.ShortCircuitRisk, EAnomalySeverity.Critical));
        var sut = CreateSut();

        // Act
        await sut.Handle(new ResolveAnomalyCommand(anomaly.AnomalyId.Value, 10));

        // Assert
        Assert.Equal(EAnomalyStatus.Active, anomaly.Status);
    }

    [Fact]
    public async Task Handle_ResolveAfterEnoughNormalReadings_ResolvesAnomalyAndPublishesEvent()
    {
        // Arrange
        var anomaly = GivenAnomaly(Anomaly());
        var sut = CreateSut();

        // Act
        await sut.Handle(new ResolveAnomalyCommand(anomaly.AnomalyId.Value, 5));

        // Assert
        Assert.Equal(EAnomalyStatus.Resolved, anomaly.Status);
        Assert.Equal(1, _mediator.CallCount(nameof(IMediator.Publish)));
    }
}

public class DeviceReadingStreamCommandServiceTests
{
    private readonly Fake<IDeviceReadingStreamRepository> _streams = new();
    private readonly Fake<IAnomalyCommandService> _anomalies = new();
    private readonly Fake<IMediator> _mediator = new();

    private DeviceReadingStreamCommandService CreateSut() => new(_streams.Object, _anomalies.Object, new Fake<IUnitOfWork>().Object,
        _mediator.Object, NullLogger<DeviceReadingStreamCommandService>.Instance);

    private DeviceReadingStream Given(DeviceReadingStream stream)
    {
        _streams.Returns(nameof(IDeviceReadingStreamRepository.FindByDeviceIdAsync), stream);
        return stream;
    }

    private static IngestDeviceReadingCommand Ingest(string id = "rd-1", float voltage = 220, string source = "EdgeDevice") =>
        new(Device, id, DateTime.UtcNow, voltage, 5, 0.95f, 60, source);

    [Fact]
    public async Task Handle_IngestForUnknownDevice_ThrowsKeyNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Ingest());

        // Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_IngestValidReading_StoresReadingAndEvaluatesAnomalies()
    {
        // Arrange
        var stream = Given(Stream());
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(Ingest());

        // Assert
        Assert.True(result);
        Assert.Single(stream.Readings);
        Assert.Equal("rd-1", _anomalies.Calls.Single().Arg<EvaluateReadingForAnomaliesCommand>().ReadingId);
    }

    [Fact]
    public async Task Handle_IngestInvalidReading_PublishesRejectionAndReturnsFalse()
    {
        // Arrange
        Given(Stream());
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(Ingest(voltage: -1));

        // Assert
        Assert.False(result);
        Assert.IsType<ReadingRejectedEvent>(_mediator.Calls.Single().Args[0]);
    }

    [Fact]
    public async Task Handle_IngestDuplicateReading_ReturnsFalse()
    {
        // Arrange
        Given(Stream());
        var sut = CreateSut();
        await sut.Handle(Ingest());

        // Act
        var result = await sut.Handle(Ingest());

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task Handle_IngestWhenAnomalyEvaluationFails_StillReturnsTrue()
    {
        // Arrange
        Given(Stream());
        _anomalies.Throws(nameof(IAnomalyCommandService.Handle), new InvalidOperationException("db"));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(Ingest());

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task Handle_ReportEdgeAnomalyWithUnknownType_ThrowsArgumentException()
    {
        // Arrange
        Given(Stream());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new ReportEdgeAnomalyCommand(Device, "Gremlins", "rd-1", "{}"));

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_ReportEdgeAnomalyForUnknownDevice_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new ReportEdgeAnomalyCommand(Device, "Overcurrent", "rd-1", "{}"));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_ReportEdgeAnomaly_EvaluatesTriggerReading()
    {
        // Arrange
        var stream = Given(Stream());
        var sut = CreateSut();

        // Act
        await sut.Handle(new ReportEdgeAnomalyCommand(Device, "overcurrent", "rd-9", "{}"));

        // Assert
        Assert.Equal(new EvaluateReadingForAnomaliesCommand(stream.StreamId.Value, "rd-9"), _anomalies.Calls.Single().Args[0]);
    }

    [Fact]
    public async Task Handle_ReplayBufferedReadings_CountsOnlyValidReadings()
    {
        // Arrange
        var stream = Given(Stream());
        var sut = CreateSut();

        // Act
        await sut.Handle(new ReplayBufferedReadingsCommand(Device, [Ingest("rd-1"), Ingest("rd-2", voltage: -5), Ingest("rd-3")]));

        // Assert
        var evt = _mediator.Calls.Single().Arg<BufferedReadingsReplayedEvent>();
        Assert.Equal((3, 2), (evt.ReadingsCount, evt.ValidReadingsCount));
        Assert.Equal(2, stream.Readings.Count);
    }

    [Fact]
    public async Task Handle_MarkDisconnectedWhenRecentlySeen_KeepsStreamActive()
    {
        // Arrange
        var stream = Given(Stream());
        var sut = CreateSut();

        // Act
        await sut.Handle(new MarkDeviceAsDisconnectedCommand(Device));

        // Assert
        Assert.Equal(EStreamStatus.Active, stream.StreamStatus);
    }

    [Fact]
    public async Task Handle_MarkDisconnectedAfterLongSilence_MarksStreamInactive()
    {
        // Arrange
        var stream = Given(Stream());
        stream.IngestReading(NormalReading(stream, "rd-old", DateTime.UtcNow.AddHours(-2)));
        var sut = CreateSut();

        // Act
        await sut.Handle(new MarkDeviceAsDisconnectedCommand(Device));

        // Assert
        Assert.Equal(EStreamStatus.Inactive, stream.StreamStatus);
    }

    [Fact]
    public async Task Handle_MarkDisconnectedAfterShortSilence_MarksStreamDegraded()
    {
        // Arrange
        var stream = Given(Stream());
        stream.IngestReading(NormalReading(stream, "rd-old", DateTime.UtcNow.AddMinutes(-15)));
        var sut = CreateSut();

        // Act
        await sut.Handle(new MarkDeviceAsDisconnectedCommand(Device));

        // Assert
        Assert.Equal(EStreamStatus.Degraded, stream.StreamStatus);
    }

    [Fact]
    public async Task Handle_UpdateThresholds_AppliesThresholdsToEveryOwnerStream()
    {
        // Arrange
        var streams = new[] { Stream(), Stream(device: "dev-2") };
        _streams.Returns(nameof(IDeviceReadingStreamRepository.FindByOwnerAsync), streams);
        var sut = CreateSut();

        // Act
        await sut.Handle(new UpdateCustomThresholdsCommand(Homeowner, 230, 6000, 25, 0.9f, 60, 15));

        // Assert
        Assert.All(streams, s => Assert.Equal((230f, 25f), (s.CustomThresholds.NominalVoltage, s.CustomThresholds.MaxCurrentAmps)));
    }

    [Fact]
    public async Task Handle_PauseStream_MarksStreamInactive()
    {
        // Arrange
        var stream = Given(Stream());
        var sut = CreateSut();

        // Act
        await sut.Handle(new PauseDeviceStreamCommand(Device, Ids.User));

        // Assert
        Assert.Equal(EStreamStatus.Inactive, stream.StreamStatus);
    }

    [Fact]
    public async Task Handle_ResumeActiveStream_ThrowsInvalidOperationException()
    {
        // Arrange
        Given(Stream());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new ResumeDeviceStreamCommand(Device, Ids.User));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_ResumePausedStream_MarksStreamActive()
    {
        // Arrange
        var stream = Given(Stream());
        stream.Pause();
        var sut = CreateSut();

        // Act
        await sut.Handle(new ResumeDeviceStreamCommand(Device, Ids.User));

        // Assert
        Assert.Equal(EStreamStatus.Active, stream.StreamStatus);
    }

    [Fact]
    public async Task Handle_PauseUnknownStream_ThrowsKeyNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new PauseDeviceStreamCommand(Device, Ids.User));

        // Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(act);
    }
}

public class RelayCommandServiceTests
{
    private readonly Fake<IRelayControlCommandRepository> _relays = new();
    private readonly Fake<IDeviceReadingStreamRepository> _streams = new();
    private readonly Fake<IProfilesContextFacade> _profiles = new();
    private readonly Fake<IServiceOperationContextFacade> _services = new();
    private readonly Fake<IMqttSubscriptionService> _mqtt = new();
    private readonly Fake<IMediator> _mediator = new();

    private RelayCommandService CreateSut() => new(_relays.Object, _streams.Object, new Fake<IAssetsContextFacade>().Object, _profiles.Object,
        _services.Object, _mqtt.Object, new Fake<IUnitOfWork>().Object, _mediator.Object, NullLogger<RelayCommandService>.Instance);

    private static IssueRelayCommandCommand TechnicianCommand() => new(Device, Property.Value, Ids.Technician, "sr-1", "Open");

    private void GivenAuthorizedTechnician()
    {
        _profiles.Returns(nameof(IProfilesContextFacade.IsTechnicianIoTCertifiedAsync), true);
        _services.Returns(nameof(IServiceOperationContextFacade.HasActiveServiceForPropertyAsync), true);
        _streams.Returns(nameof(IDeviceReadingStreamRepository.FindByDeviceIdAsync), Stream());
    }

    [Fact]
    public async Task Handle_IssueBySystem_PersistsSentCommandAndPublishesMqtt()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(new IssueRelayCommandCommand(Device, Property.Value, "SYSTEM", null!, "Open"));

        // Assert
        var relay = _relays.CallsTo(nameof(IRelayControlCommandRepository.AddAsync)).Single().Arg<RelayControlCommand>();
        Assert.Equal((ERelayCommandStatus.Sent, "SYSTEM"), (relay.Status, relay.RequestedBy));
        Assert.Contains(relay.CommandId.Value, _mqtt.Calls.Single().Arg<string>(1));
    }

    [Fact]
    public async Task Handle_IssueByUncertifiedTechnician_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(TechnicianCommand());

        // Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(act);
    }

    [Fact]
    public async Task Handle_IssueByTechnicianWithoutActiveService_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        _profiles.Returns(nameof(IProfilesContextFacade.IsTechnicianIoTCertifiedAsync), true);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(TechnicianCommand());

        // Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(act);
    }

    [Fact]
    public async Task Handle_IssueForDeviceOnAnotherProperty_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        GivenAuthorizedTechnician();
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(TechnicianCommand() with { PropertyId = "prop-2" });

        // Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(act);
    }

    [Fact]
    public async Task Handle_IssueWhenCommandAlreadyPending_ThrowsInvalidOperationException()
    {
        // Arrange
        GivenAuthorizedTechnician();
        _relays.Returns(nameof(IRelayControlCommandRepository.FindPendingByDeviceAsync), SentRelayCommand());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(TechnicianCommand());

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Empty(_mqtt.Calls);
    }

    [Fact]
    public async Task Handle_IssueByAuthorizedTechnician_RecordsTechnicianAuthorization()
    {
        // Arrange
        GivenAuthorizedTechnician();
        var sut = CreateSut();

        // Act
        await sut.Handle(TechnicianCommand());

        // Assert
        var relay = _relays.CallsTo(nameof(IRelayControlCommandRepository.AddAsync)).Single().Arg<RelayControlCommand>();
        Assert.Equal((Ids.Technician, "sr-1"), (relay.RequestedBy, relay.ServiceRequestId!.Value));
    }

    [Fact]
    public async Task Handle_AcknowledgeMissingCommand_ThrowsKeyNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new AcknowledgeRelayExecutionCommand("relay-1", Device, true, DateTime.UtcNow));

        // Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(act);
    }

    [Theory]
    [InlineData(true, ERelayCommandStatus.Executed)]
    [InlineData(false, ERelayCommandStatus.Failed)]
    public async Task Handle_AcknowledgeSentCommand_SetsStatusFromDeviceReport(bool executed, ERelayCommandStatus expected)
    {
        // Arrange
        var relay = SentRelayCommand();
        _relays.Returns(nameof(IRelayControlCommandRepository.FindByCommandIdAsync), relay);
        var sut = CreateSut();

        // Act
        await sut.Handle(new AcknowledgeRelayExecutionCommand(relay.CommandId.Value, Device, executed, DateTime.UtcNow));

        // Assert
        Assert.Equal(expected, relay.Status);
    }
}

public class OfflineReadingsSyncCommandServiceTests
{
    private readonly Fake<IEquipmentTelemetryRepository> _telemetry = new();
    private readonly Fake<IDeviceReadingStreamCommandService> _legacy = new();

    private OfflineReadingsSyncCommandService CreateSut() => new(
        new EquipmentTelemetryService(null!, _telemetry.Object, null!, new ConfigurationBuilder().Build(), Alerts()), _legacy.Object,
        NullLogger<OfflineReadingsSyncCommandService>.Instance);

    private static AlertMaterializer Alerts() => new(new Fake<IAlertRepository>().Object, new Fake<IAlertDispatcher>().Object,
        new Fake<IMediator>().Object, new Fake<IOnDutyWorkerPort>().Object, NullLogger<AlertMaterializer>.Instance);

    private static OfflineReading Reading(string id, DateTime? at = null, string device = Device) =>
        new(device, id, at ?? DateTime.UtcNow, 220, 5, 0.95f, 60, "EdgeBufferReplay", null);

    [Fact]
    public async Task Handle_WithoutReadings_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new SyncOfflineReadingsBatchCommand("panel-1", []), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_WithReadingMissingId_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new SyncOfflineReadingsBatchCommand("panel-1", [Reading(" ")]), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_UnboundDeviceReadings_ReplaysThroughLegacyStreamInChronologicalOrder()
    {
        // Arrange
        var now = DateTime.UtcNow;
        _legacy.Returns(nameof(IDeviceReadingStreamCommandService.Handle), true);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new SyncOfflineReadingsBatchCommand("panel-1", [Reading("rd-2", now), Reading("rd-1", now.AddMinutes(-5))]),
            CancellationToken.None);

        // Assert
        Assert.Equal((2, 2, "panel-1"), (result.Received, result.Accepted, result.PanelId));
        Assert.Equal(["rd-1", "rd-2"], _legacy.Calls.Select(c => c.Arg<IngestDeviceReadingCommand>().ReadingId));
    }

    [Fact]
    public async Task Handle_WhenLegacyStreamRejectsReading_ReportsRejection()
    {
        // Arrange
        _legacy.Returns(nameof(IDeviceReadingStreamCommandService.Handle), false);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new SyncOfflineReadingsBatchCommand(null, [Reading("rd-1")]), CancellationToken.None);

        // Assert
        Assert.Equal((1, EdgeReadingOutcome.Rejected), (result.Rejected, result.Results.Single().Status));
    }

    [Fact]
    public async Task Handle_WhenLegacyDeviceIsUnknown_ReportsRejectionWithReason()
    {
        // Arrange
        _legacy.Throws(nameof(IDeviceReadingStreamCommandService.Handle), new KeyNotFoundException("Device dev-1 is not registered."));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new SyncOfflineReadingsBatchCommand(null, [Reading("rd-1")]), CancellationToken.None);

        // Assert
        Assert.Equal("Device dev-1 is not registered.", result.Results.Single().Reason);
    }

    [Fact]
    public async Task Handle_WhenDeviceHasAnotherIdentity_RejectsWithoutLegacyReplay()
    {
        // Arrange
        _telemetry.Returns(nameof(IEquipmentTelemetryRepository.HasOtherIdentity), true);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new SyncOfflineReadingsBatchCommand(null, [Reading("rd-1")]), CancellationToken.None);

        // Assert
        Assert.Equal(1, result.Rejected);
        Assert.Empty(_legacy.Calls);
    }
}
