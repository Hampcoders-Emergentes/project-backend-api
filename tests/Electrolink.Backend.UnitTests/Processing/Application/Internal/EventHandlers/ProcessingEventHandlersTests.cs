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

namespace Electrolink.Backend.UnitTests.Processing.Application.Internal.EventHandlers;

public class ConsumptionThresholdsUpdatedEventHandlerTests
{
    private const string DemoDevice = "dev-electrolink-esp32-01";
    private readonly Fake<IDeviceReadingStreamCommandService> _commands = new();
    private readonly Fake<IDeviceReadingStreamRepository> _streams = new();

    private ConsumptionThresholdsUpdatedEventHandler CreateSut() => new(_commands.Object, _streams.Object, new Fake<IUnitOfWork>().Object,
        NullLogger<ConsumptionThresholdsUpdatedEventHandler>.Instance);

    [Fact]
    public async Task Handle_WithPartialThresholds_UsesDefaultsForMissingValues()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(new ConsumptionThresholdsUpdatedIntegrationEvent(Homeowner, new Dictionary<string, decimal> { ["maxCurrentAmps"] = 30 },
            DateTime.UtcNow), CancellationToken.None);

        // Assert
        var command = _commands.Calls.Single().Arg<UpdateCustomThresholdsCommand>();
        Assert.Equal((30f, 220f, 10, 0.60f), (command.MaxCurrentAmps, command.NominalVoltage, command.DisconnectionThresholdMin, command.AlertLimitAmps));
    }

    [Fact]
    public async Task Handle_WhenOwnerHasDemoDevice_UpdatesDemoStreamThresholds()
    {
        // Arrange
        var demo = Stream(device: DemoDevice);
        _streams.Returns(nameof(IDeviceReadingStreamRepository.FindByDeviceIdAsync), demo);
        var sut = CreateSut();

        // Act
        await sut.Handle(new ConsumptionThresholdsUpdatedIntegrationEvent(Homeowner, new Dictionary<string, decimal> { ["nominalVoltage"] = 230 },
            DateTime.UtcNow), CancellationToken.None);

        // Assert
        Assert.Equal(230f, demo.CustomThresholds.NominalVoltage);
    }

    [Fact]
    public async Task Handle_WhenDemoDeviceBelongsToAnotherOwner_LeavesDemoStreamUnchanged()
    {
        // Arrange
        var demo = Stream(ClientIdentity.FromHomeowner("ho-2"), DemoDevice);
        _streams.Returns(nameof(IDeviceReadingStreamRepository.FindByDeviceIdAsync), demo);
        var sut = CreateSut();

        // Act
        await sut.Handle(new ConsumptionThresholdsUpdatedIntegrationEvent(Homeowner, new Dictionary<string, decimal> { ["nominalVoltage"] = 230 },
            DateTime.UtcNow), CancellationToken.None);

        // Assert
        Assert.Equal(220f, demo.CustomThresholds.NominalVoltage);
    }
}

public class ProcessingDeviceLifecycleEventHandlersTests
{
    private readonly Fake<IDeviceReadingStreamRepository> _streams = new();
    private readonly Fake<IAssetsPort> _assets = new();

    private static DeviceInstalledEvent Installed() =>
        new(Device, "SN-1", Property.Value, Ids.Technician, "req-1", "1.0", DateTime.UtcNow);

    [Fact]
    public async Task DeviceDecommissionedHandle_WithStream_PausesStream()
    {
        // Arrange
        var stream = Stream();
        _streams.Returns(nameof(IDeviceReadingStreamRepository.FindByDeviceIdAsync), stream);
        var sut = new DeviceDecommissionedEventHandler(_streams.Object, new Fake<IUnitOfWork>().Object,
            NullLogger<DeviceDecommissionedEventHandler>.Instance);

        // Act
        await sut.Handle(new DeviceDecommissionedEvent(Device, "SN-1", Property.Value, EDeviceStatus.Installed, "broken", DateTime.UtcNow),
            CancellationToken.None);

        // Assert
        Assert.Equal(EStreamStatus.Inactive, stream.StreamStatus);
    }

    [Fact]
    public async Task DeviceDecommissionedHandle_WithoutStream_DoesNotUpdate()
    {
        // Arrange
        var sut = new DeviceDecommissionedEventHandler(_streams.Object, new Fake<IUnitOfWork>().Object,
            NullLogger<DeviceDecommissionedEventHandler>.Instance);

        // Act
        await sut.Handle(new DeviceDecommissionedEvent(Device, "SN-1", null, EDeviceStatus.InStock, "broken", DateTime.UtcNow),
            CancellationToken.None);

        // Assert
        Assert.Equal(0, _streams.CallCount(nameof(IDeviceReadingStreamRepository.UpdateAsync)));
    }

    [Fact]
    public async Task DeviceInstalledHandle_WithPausedStream_ResumesStream()
    {
        // Arrange
        var stream = Stream();
        stream.Pause();
        _streams.Returns(nameof(IDeviceReadingStreamRepository.FindByDeviceIdAsync), stream);
        var sut = new DeviceInstalledEventHandler(_streams.Object, _assets.Object, new Fake<IUnitOfWork>().Object,
            NullLogger<DeviceInstalledEventHandler>.Instance);

        // Act
        await sut.Handle(Installed(), CancellationToken.None);

        // Assert
        Assert.Equal(EStreamStatus.Active, stream.StreamStatus);
    }

    [Fact]
    public async Task DeviceInstalledHandle_WithoutStream_CreatesStreamForDeviceOwner()
    {
        // Arrange
        _assets.Returns(nameof(IAssetsPort.GetInstalledDeviceInfoAsync), new DeviceInfoDto(Device, "INSTALLED", Property.Value, Homeowner));
        var sut = new DeviceInstalledEventHandler(_streams.Object, _assets.Object, new Fake<IUnitOfWork>().Object,
            NullLogger<DeviceInstalledEventHandler>.Instance);

        // Act
        await sut.Handle(Installed(), CancellationToken.None);

        // Assert
        var stream = _streams.CallsTo(nameof(IDeviceReadingStreamRepository.AddAsync)).Single().Arg<DeviceReadingStream>();
        Assert.Equal((Device, Owner), (stream.DeviceId.Value, stream.Owner));
    }

    [Fact]
    public async Task DeviceInstalledHandle_WhenDeviceInfoUnavailable_CreatesNoStream()
    {
        // Arrange
        var sut = new DeviceInstalledEventHandler(_streams.Object, _assets.Object, new Fake<IUnitOfWork>().Object,
            NullLogger<DeviceInstalledEventHandler>.Instance);

        // Act
        await sut.Handle(Installed(), CancellationToken.None);

        // Assert
        Assert.Equal(0, _streams.CallCount(nameof(IDeviceReadingStreamRepository.AddAsync)));
    }
}
