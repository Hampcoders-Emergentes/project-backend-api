using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Assets.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Events;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Events;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Events;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using static Electrolink.Backend.UnitTests.Assets.AssetsTestData;

namespace Electrolink.Backend.UnitTests.Assets.Application.Internal.EventHandlers;

/// <summary>Handlers that only log: the observable contract is that they complete without side effects or errors.</summary>
public class AssetsLoggingEventHandlersTests
{
    [Fact]
    public async Task DeviceAssignedToPropertyHandle_WithEvent_CompletesSuccessfully()
    {
        // Arrange
        var property = NewProperty();
        var device = InStockDevice();
        device.AssignToProperty(property.Id, InstallationRequestId.From(InstallationRequest));
        var evt = device.DomainEvents.OfType<DeviceAssignedToPropertyEvent>().Single();
        var sut = new DeviceAssignedToPropertyEventHandler(NullLogger<DeviceAssignedToPropertyEventHandler>.Instance);

        // Act
        var task = sut.Handle(evt, CancellationToken.None);

        // Assert
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DeviceConnectionStatusUpdatedHandle_WithEvent_CompletesSuccessfully()
    {
        // Arrange
        var device = InstalledDevice(NewProperty());
        device.UpdateConnectionStatus(EConnectionStatus.Connected, DateTime.UtcNow);
        var evt = device.DomainEvents.OfType<DeviceConnectionStatusUpdatedEvent>().Single();
        var sut = new DeviceConnectionStatusUpdatedEventHandler(NullLogger<DeviceConnectionStatusUpdatedEventHandler>.Instance);

        // Act
        var task = sut.Handle(evt, CancellationToken.None);

        // Assert
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DeviceDecommissionedHandle_WithEvent_CompletesSuccessfully()
    {
        // Arrange
        var device = InStockDevice();
        device.Decommission("broken");
        var evt = device.DomainEvents.OfType<DeviceDecommissionedEvent>().Single();
        var sut = new DeviceDecommissionedEventHandler(NullLogger<DeviceDecommissionedEventHandler>.Instance);

        // Act
        var task = sut.Handle(evt, CancellationToken.None);

        // Assert
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DeviceInstalledHandle_WithEvent_CompletesSuccessfully()
    {
        // Arrange
        var property = NewProperty();
        var device = AssignedDevice(property);
        device.RecordInstallation(TechnicianId.From(Ids.Technician), property.Id, "1.1", DateTime.UtcNow);
        var evt = device.DomainEvents.OfType<DeviceInstalledEvent>().Single();
        var sut = new DeviceInstalledEventHandler(NullLogger<DeviceInstalledEventHandler>.Instance);

        // Act
        var task = sut.Handle(evt, CancellationToken.None);

        // Assert
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DeviceReinstalledHandle_WithEvent_CompletesSuccessfully()
    {
        // Arrange
        var device = InstalledDevice(NewProperty());
        device.SendToMaintenance("noise", null);
        device.Reinstall(TechnicianId.From(Ids.Technician), "2.0", DateTime.UtcNow);
        var evt = device.DomainEvents.OfType<DeviceReinstalledEvent>().Single();
        var sut = new DeviceReinstalledEventHandler(NullLogger<DeviceReinstalledEventHandler>.Instance);

        // Act
        var task = sut.Handle(evt, CancellationToken.None);

        // Assert
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DeviceSentToMaintenanceHandle_WithEvent_CompletesSuccessfully()
    {
        // Arrange
        var device = InstalledDevice(NewProperty());
        device.SendToMaintenance("noise", null);
        var evt = device.DomainEvents.OfType<DeviceSentToMaintenanceEvent>().Single();
        var sut = new DeviceSentToMaintenanceEventHandler(NullLogger<DeviceSentToMaintenanceEventHandler>.Instance);

        // Act
        var task = sut.Handle(evt, CancellationToken.None);

        // Assert
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task IoTDeviceRegisteredHandle_WithEvent_CompletesSuccessfully()
    {
        // Arrange
        var evt = InStockDevice().DomainEvents.OfType<IoTDeviceRegisteredEvent>().Single();
        var sut = new IoTDeviceRegisteredEventHandler(NullLogger<IoTDeviceRegisteredEventHandler>.Instance);

        // Act
        var task = sut.Handle(evt, CancellationToken.None);

        // Assert
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }
}

public class DeviceConnectivityEventHandlersTests
{
    [Fact]
    public async Task DeviceDisconnectedHandle_WithEvent_MarksDeviceDisconnected()
    {
        // Arrange
        var commands = new Fake<IIoTDeviceCommandService>();
        var lastReading = DateTime.UtcNow.AddMinutes(-5);
        var sut = new DeviceDisconnectedEventHandler(commands.Object);

        // Act
        await sut.Handle(new DeviceDisconnectedExternalEvent("dev-1", lastReading), CancellationToken.None);

        // Assert
        Assert.Equal(new UpdateDeviceConnectionStatusCommand("dev-1", EConnectionStatus.Disconnected, lastReading),
            commands.Calls.Single().Arg<UpdateDeviceConnectionStatusCommand>());
    }

    [Fact]
    public async Task DeviceReconnectedHandle_WithEvent_MarksDeviceConnectedAtReconnectionTime()
    {
        // Arrange
        var commands = new Fake<IIoTDeviceCommandService>();
        var reconnectedAt = DateTime.UtcNow;
        var sut = new DeviceReconnectedEventHandler(commands.Object);

        // Act
        await sut.Handle(new DeviceReconnectedExternalEvent("dev-1", reconnectedAt), CancellationToken.None);

        // Assert
        Assert.Equal(new UpdateDeviceConnectionStatusCommand("dev-1", EConnectionStatus.Connected, reconnectedAt),
            commands.Calls.Single().Arg<UpdateDeviceConnectionStatusCommand>());
    }
}

public class ProfileCompletedEventHandlerTests
{
    private readonly Fake<IPropertyPortfolioCommandService> _portfolios = new();
    private readonly Fake<ITechnicianInventoryCommandService> _inventories = new();

    private ProfileCompletedEventHandler CreateSut() =>
        new(_portfolios.Object, _inventories.Object, NullLogger<ProfileCompletedEventHandler>.Instance);

    private static ProfileCompletedEvent Event(string subject, EBusinessRole role) =>
        new("prof-1", Ids.User, subject, role, DateTime.UtcNow);

    [Fact]
    public async Task Handle_TechnicianProfile_CreatesTechnicianInventory()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(Event(Ids.Technician, EBusinessRole.Technician), CancellationToken.None);

        // Assert
        Assert.Equal(Ids.Technician, _inventories.Calls.Single().Arg<CreateTechnicianInventoryCommand>().TechnicianId.Value);
    }

    [Fact]
    public async Task Handle_HomeownerProfile_CreatesHomeownerPortfolio()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(Event(Homeowner, EBusinessRole.HomeOwner), CancellationToken.None);

        // Assert
        Assert.Equal(ClientIdentity.FromHomeowner(Homeowner), _portfolios.Calls.Single().Arg<CreatePropertyPortfolioCommand>().Owner);
    }

    [Fact]
    public async Task Handle_CompanyProfile_CreatesCompanyPortfolio()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(Event(Company, EBusinessRole.Company), CancellationToken.None);

        // Assert
        Assert.Equal(ClientIdentity.FromCompany(Company), _portfolios.Calls.Single().Arg<CreatePropertyPortfolioCommand>().Owner);
    }

    [Fact]
    public async Task Handle_WhenInventoryCreationFails_SwallowsException()
    {
        // Arrange
        _inventories.Throws(nameof(ITechnicianInventoryCommandService.Handle), new InvalidOperationException("db down"));
        var sut = CreateSut();

        // Act
        var exception = await Record.ExceptionAsync(() => sut.Handle(Event(Ids.Technician, EBusinessRole.Technician), CancellationToken.None));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task Handle_WhenPortfolioAlreadyExists_SwallowsException()
    {
        // Arrange
        _portfolios.Throws(nameof(IPropertyPortfolioCommandService.Handle), new InvalidOperationException("Portfolio already exists"));
        var sut = CreateSut();

        // Act
        var exception = await Record.ExceptionAsync(() => sut.Handle(Event(Homeowner, EBusinessRole.HomeOwner), CancellationToken.None));

        // Assert
        Assert.Null(exception);
    }
}

public class ServiceCompletedIoTInstallationEventHandlerTests
{
    private readonly Fake<IIoTDeviceCommandService> _devices = new();
    private readonly Fake<IPropertyCommandService> _properties = new();
    private readonly string _property = PropertyId.NewPropertyId().Value;
    private readonly string _assignment = AssignmentId.NewAssignmentId().Value;

    private ServiceCompletedIoTInstallationEventHandler CreateSut() =>
        new(_devices.Object, _properties.Object, NullLogger<ServiceCompletedIoTInstallationEventHandler>.Instance);

    private ServiceCompletedExternalEvent Event(string type, string? firmware = "2.0") =>
        new(_assignment, type, _property, Ids.Technician, "dev-1", firmware, DateTime.UtcNow);

    [Fact]
    public async Task Handle_NonInstallationService_IgnoresEvent()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(Event("REPAIR"), CancellationToken.None);

        // Assert
        Assert.Empty(_devices.Calls);
        Assert.Empty(_properties.Calls);
    }

    [Fact]
    public async Task Handle_InstallationService_RecordsDeviceInstallation()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(Event("IOT_INSTALLATION"), CancellationToken.None);

        // Assert
        var command = _devices.Calls.Single().Arg<RecordDeviceInstallationCommand>();
        Assert.Equal(("dev-1", _property, _assignment, "2.0"), (command.DeviceId, command.PropertyId, command.InstallationRequestId,
            command.FirmwareVersion));
    }

    [Fact]
    public async Task Handle_InstallationWithoutFirmware_UsesUnknownFirmware()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(Event("IOT_INSTALLATION", firmware: null), CancellationToken.None);

        // Assert
        Assert.Equal("unknown", _devices.Calls.Single().Arg<RecordDeviceInstallationCommand>().FirmwareVersion);
    }

    [Fact]
    public async Task Handle_InstallationService_RecordsPropertyMaintenance()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(Event("IOT_INSTALLATION"), CancellationToken.None);

        // Assert
        var command = _properties.Calls.Single().Arg<RecordMaintenanceForPropertyCommand>();
        Assert.Equal((_property, _assignment), (command.PropertyId.Value, command.AssignmentId.Value));
    }
}
