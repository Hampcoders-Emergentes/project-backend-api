using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Assets.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Interfaces.ACL;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using static Electrolink.Backend.UnitTests.Assets.AssetsTestData;

namespace Electrolink.Backend.UnitTests.Assets.Application.Internal.CommandServices;

public class DeviceAssignmentServiceTests
{
    private readonly Fake<IIoTDeviceRepository> _devices = new();
    private readonly Fake<IPropertyRepository> _properties = new();
    private readonly Fake<IProfilesContextFacade> _profiles = TechnicianProfiles();
    private readonly Fake<IInstallationAssignmentReader> _installations = new();
    private readonly Fake<IIoTDeviceCommandService> _commands = new();
    private readonly Property _property = NewProperty();

    public DeviceAssignmentServiceTests()
    {
        _properties.Returns(nameof(IPropertyRepository.FindByIdAsync), _property);
        _installations.Returns(nameof(IInstallationAssignmentReader.IsAssignedInstallation), true);
    }

    private DeviceAssignmentService CreateSut() =>
        new(_devices.Object, _properties.Object, _profiles.Object, _installations.Object, _commands.Object);

    private AssignRequestedDeviceCommand Command(IoTDevice device) =>
        new(Ids.Actor, device.Id.Value, _property.Id.Value, InstallationRequest);

    [Fact]
    public async Task Handle_WithOwnFreeStockDevice_DelegatesAssignmentCommand()
    {
        // Arrange
        var device = InStockDevice(Ids.Technician);
        _devices.Returns(nameof(IIoTDeviceRepository.FindByIdAsync), device);
        _commands.Returns(nameof(IIoTDeviceCommandService.Handle), device);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(Command(device));

        // Assert
        Assert.Same(device, result);
        Assert.Equal(new AssignDeviceToPropertyCommand(device.Id.Value, _property.Id.Value, InstallationRequest),
            _commands.Calls.Single().Arg<AssignDeviceToPropertyCommand>());
    }

    [Fact]
    public async Task Handle_WhenActorIsNotTechnician_ThrowsDeviceAssignmentUnavailableException()
    {
        // Arrange
        var device = InStockDevice(Ids.Technician);
        _profiles.Returns(nameof(IProfilesContextFacade.GetProfileClaimsAsync), Claims("HomeOwner", Homeowner));
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Command(device));

        // Assert
        await Assert.ThrowsAsync<DeviceAssignmentUnavailableException>(act);
    }

    [Fact]
    public async Task Handle_WhenInstallationIsNotAssignedToTechnician_ThrowsDeviceAssignmentUnavailableException()
    {
        // Arrange
        var device = InStockDevice(Ids.Technician);
        _installations.Returns(nameof(IInstallationAssignmentReader.IsAssignedInstallation), false);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Command(device));

        // Assert
        await Assert.ThrowsAsync<DeviceAssignmentUnavailableException>(act);
    }

    [Fact]
    public async Task Handle_WhenDeviceBelongsToAnotherTechnician_ThrowsDeviceAssignmentUnavailableException()
    {
        // Arrange
        var device = InStockDevice("tech-other");
        _devices.Returns(nameof(IIoTDeviceRepository.FindByIdAsync), device);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Command(device));

        // Assert
        await Assert.ThrowsAsync<DeviceAssignmentUnavailableException>(act);
    }

    [Fact]
    public async Task Handle_WhenDeviceIsAlreadyAssigned_ThrowsDeviceAssignmentConflictException()
    {
        // Arrange
        var device = AssignedDevice(_property, Ids.Technician);
        _devices.Returns(nameof(IIoTDeviceRepository.FindByIdAsync), device);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Command(device));

        // Assert
        await Assert.ThrowsAsync<DeviceAssignmentConflictException>(act);
    }

    [Fact]
    public async Task Handle_WhenDeviceIsMissing_ThrowsDeviceAssignmentUnavailableException()
    {
        // Arrange
        var device = InStockDevice(Ids.Technician);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Command(device));

        // Assert
        await Assert.ThrowsAsync<DeviceAssignmentUnavailableException>(act);
    }
}

public class DeviceInstallationServiceTests
{
    private readonly Fake<IIoTDeviceRepository> _devices = new();
    private readonly Fake<IPropertyRepository> _properties = new();
    private readonly Fake<IProfilesContextFacade> _profiles = TechnicianProfiles();
    private readonly Fake<IInstallationAssignmentReader> _installations = new();
    private readonly Fake<IIoTDeviceCommandService> _commands = new();
    private readonly Property _property = NewProperty();

    public DeviceInstallationServiceTests()
    {
        _properties.Returns(nameof(IPropertyRepository.FindByIdAsync), _property);
        _installations.Returns(nameof(IInstallationAssignmentReader.IsAssignedInstallation), true);
    }

    private DeviceInstallationService CreateSut() =>
        new(_devices.Object, _properties.Object, _profiles.Object, _installations.Object, _commands.Object);

    private RecordAssignedDeviceInstallationCommand Command(IoTDevice device, string technician = Ids.Technician) =>
        new(Ids.Actor, device.Id.Value, _property.Id.Value, technician, "2.0", DateTime.UtcNow);

    [Fact]
    public async Task Handle_WithAssignedDevice_DelegatesInstallationWithRequestId()
    {
        // Arrange
        var device = AssignedDevice(_property);
        _devices.Returns(nameof(IIoTDeviceRepository.FindByIdAsync), device);
        _commands.Returns(nameof(IIoTDeviceCommandService.Handle), device);
        var sut = CreateSut();

        // Act
        await sut.Handle(Command(device));

        // Assert
        var delegated = _commands.Calls.Single().Arg<RecordDeviceInstallationCommand>();
        Assert.Equal((Ids.Technician, InstallationRequest, "2.0"), (delegated.TechnicianId, delegated.InstallationRequestId, delegated.FirmwareVersion));
    }

    [Fact]
    public async Task Handle_WhenCommandTechnicianDiffersFromProfile_ThrowsDeviceInstallationUnavailableException()
    {
        // Arrange
        var device = AssignedDevice(_property);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Command(device, "tech-other"));

        // Assert
        await Assert.ThrowsAsync<DeviceInstallationUnavailableException>(act);
    }

    [Fact]
    public async Task Handle_WhenDeviceIsNotAssignedToProperty_ThrowsDeviceInstallationUnavailableException()
    {
        // Arrange
        var device = InStockDevice();
        _devices.Returns(nameof(IIoTDeviceRepository.FindByIdAsync), device);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Command(device));

        // Assert
        await Assert.ThrowsAsync<DeviceInstallationUnavailableException>(act);
    }

    [Fact]
    public async Task Handle_WhenDeviceIsAlreadyInstalled_ThrowsDeviceInstallationStateException()
    {
        // Arrange
        var device = InstalledDevice(_property);
        _devices.Returns(nameof(IIoTDeviceRepository.FindByIdAsync), device);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Command(device));

        // Assert
        await Assert.ThrowsAsync<DeviceInstallationStateException>(act);
    }

    [Fact]
    public async Task Handle_WhenInstallationIsNotAssigned_ThrowsDeviceInstallationUnavailableException()
    {
        // Arrange
        var device = AssignedDevice(_property);
        _devices.Returns(nameof(IIoTDeviceRepository.FindByIdAsync), device);
        _installations.Returns(nameof(IInstallationAssignmentReader.IsAssignedInstallation), false);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Command(device));

        // Assert
        await Assert.ThrowsAsync<DeviceInstallationUnavailableException>(act);
    }
}
