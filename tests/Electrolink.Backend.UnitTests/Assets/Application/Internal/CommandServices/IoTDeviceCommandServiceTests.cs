using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Assets.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using MediatR;
using static Electrolink.Backend.UnitTests.Assets.AssetsTestData;

namespace Electrolink.Backend.UnitTests.Assets.Application.Internal.CommandServices;

public class IoTDeviceCommandServiceTests
{
    private readonly Fake<IIoTDeviceRepository> _devices = new();
    private readonly Fake<IPropertyRepository> _properties = new();
    private readonly Fake<IUnitOfWork> _unitOfWork = new();
    private readonly Fake<IPublisher> _publisher = new();
    private readonly Fake<IProfilesContextFacade> _profiles = HomeownerProfiles();
    private readonly Property _property = NewProperty();

    private IoTDeviceCommandService CreateSut() =>
        new(_devices.Object, _properties.Object, _unitOfWork.Object, _publisher.Object, _profiles.Object);

    private void GivenDevice(IoTDevice device) => _devices.Returns(nameof(IIoTDeviceRepository.FindByIdAsync), device);

    [Fact]
    public async Task Handle_RegisterWithDuplicateSerial_ThrowsDuplicateAssetException()
    {
        // Arrange
        _devices.Returns(nameof(IIoTDeviceRepository.FindBySerialNumberAsync), InStockDevice());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new RegisterIoTDeviceCommand("SN-1", "secret", "1.0"));

        // Assert
        await Assert.ThrowsAsync<DuplicateAssetException>(act);
    }

    [Fact]
    public async Task Handle_RegisterWithNewSerial_ReturnsInStockDeviceWithHashedKey()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var device = await sut.Handle(new RegisterIoTDeviceCommand("SN-1", "secret", "1.0"));

        // Assert
        Assert.Equal(EDeviceStatus.InStock, device.Status);
        Assert.True(device.ApiKeyHash.Verify("secret"));
    }

    [Fact]
    public async Task Handle_RegisterWithNewSerial_PublishesRegistrationEvent()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var device = await sut.Handle(new RegisterIoTDeviceCommand("SN-1", "secret", "1.0"));

        // Assert
        Assert.Equal(1, _publisher.CallCount(nameof(IPublisher.Publish)));
        Assert.Empty(device.DomainEvents);
    }

    [Fact]
    public async Task Handle_RegisterForTechnician_AssignsStockOwner()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var device = await sut.Handle(new RegisterTechnicianIoTDeviceCommand(TechnicianId.From(Ids.Technician), "SN-1", "secret", "1.0"));

        // Assert
        Assert.Equal(Ids.Technician, device.StockTechnicianId!.Value);
    }

    [Fact]
    public async Task Handle_AssignWhenDeviceMissing_ThrowsAssetNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new AssignDeviceToPropertyCommand("dev-1", _property.Id.Value, InstallationRequest));

        // Assert
        await Assert.ThrowsAsync<AssetNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_AssignWhenPropertyMissing_ThrowsAssetNotFoundException()
    {
        // Arrange
        var device = InStockDevice();
        GivenDevice(device);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new AssignDeviceToPropertyCommand(device.Id.Value, _property.Id.Value, InstallationRequest));

        // Assert
        await Assert.ThrowsAsync<AssetNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_AssignWithStockDevice_LinksDeviceAndProperty()
    {
        // Arrange
        var device = InStockDevice();
        GivenDevice(device);
        _properties.Returns(nameof(IPropertyRepository.FindByIdAsync), _property);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new AssignDeviceToPropertyCommand(device.Id.Value, _property.Id.Value, InstallationRequest));

        // Assert
        Assert.Equal((EDeviceStatus.Assigned, _property.Id), (result.Status, result.AssignedPropertyId));
        Assert.Contains(device.Id.Value, _property.InstalledDeviceIds);
    }

    [Fact]
    public async Task Handle_RecordInstallationWithAssignedDevice_MarksDeviceInstalled()
    {
        // Arrange
        var device = AssignedDevice(_property);
        GivenDevice(device);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new RecordDeviceInstallationCommand(device.Id.Value, _property.Id.Value, Ids.Technician,
            InstallationRequest, "2.0", DateTime.UtcNow));

        // Assert
        Assert.Equal((EDeviceStatus.Installed, "2.0"), (result.Status, result.FirmwareVersion));
    }

    [Fact]
    public async Task Handle_RecordInstallationWhenDeviceMissing_ThrowsAssetNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new RecordDeviceInstallationCommand("dev-1", _property.Id.Value, Ids.Technician, null, "2.0",
            DateTime.UtcNow));

        // Assert
        await Assert.ThrowsAsync<AssetNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_UpdateConnectionStatusOnInstalledDevice_StoresStatusAndReadingTime()
    {
        // Arrange
        var device = InstalledDevice(_property);
        GivenDevice(device);
        var readingAt = DateTime.UtcNow;
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new UpdateDeviceConnectionStatusCommand(device.Id.Value, EConnectionStatus.Connected, readingAt));

        // Assert
        Assert.Equal((EConnectionStatus.Connected, (DateTime?)readingAt), (result.ConnectionStatus, result.LastReadingAt));
    }

    [Fact]
    public async Task Handle_UpdateConnectionStatusOnStockDevice_ThrowsInvalidOperationException()
    {
        // Arrange
        var device = InStockDevice();
        GivenDevice(device);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new UpdateDeviceConnectionStatusCommand(device.Id.Value, EConnectionStatus.Connected, null));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_SendToMaintenanceOnInstalledDevice_MovesDeviceToMaintenance()
    {
        // Arrange
        var device = InstalledDevice(_property);
        GivenDevice(device);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new SendDeviceToMaintenanceCommand(device.Id.Value, "noise", null));

        // Assert
        Assert.Equal((EDeviceStatus.Maintenance, "noise"), (result.Status, result.MaintenanceReason));
    }

    [Fact]
    public async Task Handle_ReinstallFromMaintenance_ReturnsInstalledDevice()
    {
        // Arrange
        var device = InstalledDevice(_property);
        device.SendToMaintenance("noise", null);
        GivenDevice(device);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new ReinstallDeviceCommand(device.Id.Value, Ids.Technician, "3.0", DateTime.UtcNow));

        // Assert
        Assert.Equal((EDeviceStatus.Installed, (string?)null), (result.Status, result.MaintenanceReason));
    }

    [Fact]
    public async Task Handle_DecommissionAssignedDevice_RemovesDeviceFromProperty()
    {
        // Arrange
        var device = InStockDevice();
        _properties.Returns(nameof(IPropertyRepository.FindByIdAsync), _property);
        GivenDevice(device);
        await CreateSut().Handle(new AssignDeviceToPropertyCommand(device.Id.Value, _property.Id.Value, InstallationRequest));
        var sut = CreateSut();

        // Act
        await sut.Handle(new DecommissionDeviceCommand(device.Id.Value, "broken"));

        // Assert
        Assert.Equal(EDeviceStatus.Decommissioned, device.Status);
        Assert.DoesNotContain(device.Id.Value, _property.InstalledDeviceIds);
    }

    [Fact]
    public async Task Handle_DecommissionWhenDeviceMissing_ThrowsAssetNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new DecommissionDeviceCommand("dev-1", "broken"));

        // Assert
        await Assert.ThrowsAsync<AssetNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_DecommissionOwnedDeviceByOwner_DecommissionsDevice()
    {
        // Arrange
        var device = AssignedDevice(_property);
        GivenDevice(device);
        _properties.Returns(nameof(IPropertyRepository.FindByIdAndOwnerAsync), _property);
        var sut = CreateSut();

        // Act
        await sut.Handle(new DecommissionOwnedDeviceCommand(Ids.Actor, device.Id.Value, "upgrade"));

        // Assert
        Assert.Equal(EDeviceStatus.Decommissioned, device.Status);
    }

    [Fact]
    public async Task Handle_DecommissionOwnedDeviceOfAnotherOwner_ThrowsDeviceRetirementUnavailableException()
    {
        // Arrange
        var device = AssignedDevice(_property);
        GivenDevice(device);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new DecommissionOwnedDeviceCommand(Ids.Actor, device.Id.Value, "upgrade"));

        // Assert
        await Assert.ThrowsAsync<DeviceRetirementUnavailableException>(act);
    }

    [Fact]
    public async Task Handle_DecommissionOwnedDeviceWithInactiveProfile_ThrowsDeviceRetirementUnavailableException()
    {
        // Arrange
        var device = AssignedDevice(_property);
        GivenDevice(device);
        _properties.Returns(nameof(IPropertyRepository.FindByIdAndOwnerAsync), _property);
        _profiles.Returns(nameof(IProfilesContextFacade.GetProfileClaimsAsync), Claims("HomeOwner", Homeowner, status: "Pending"));
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new DecommissionOwnedDeviceCommand(Ids.Actor, device.Id.Value, "upgrade"));

        // Assert
        await Assert.ThrowsAsync<DeviceRetirementUnavailableException>(act);
    }
}
