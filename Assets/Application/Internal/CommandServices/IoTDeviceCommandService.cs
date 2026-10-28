using Hampcoders.Electrolink.API.Assets.Application.Internal.Authorization;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using MediatR;

namespace Hampcoders.Electrolink.API.Assets.Application.Internal.CommandServices;

public class IoTDeviceCommandService : IIoTDeviceCommandService
{
    private readonly IIoTDeviceRepository _deviceRepository;
    private readonly IPropertyRepository _propertyRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;
    private readonly IProfilesContextFacade _profiles;

    public IoTDeviceCommandService(
        IIoTDeviceRepository deviceRepository,
        IPropertyRepository propertyRepository,
        IUnitOfWork unitOfWork,
        IPublisher publisher,
        IProfilesContextFacade profiles)
    {
        _deviceRepository = deviceRepository;
        _propertyRepository = propertyRepository;
        _unitOfWork = unitOfWork;
        _publisher = publisher;
        _profiles = profiles;
    }

    public Task<IoTDevice> Handle(RegisterIoTDeviceCommand command) => Register(command, null);

    public Task<IoTDevice> Handle(RegisterTechnicianIoTDeviceCommand command) =>
        Register(new RegisterIoTDeviceCommand(command.SerialNumber, command.PlainTextApiKey, command.FirmwareVersion), command.Technician);

    private async Task<IoTDevice> Register(RegisterIoTDeviceCommand command, TechnicianId? technician)
    {
        var serial = SerialNumber.From(command.SerialNumber);

        var existing = await _deviceRepository.FindBySerialNumberAsync(serial);
        if (existing is not null)
            throw new DuplicateAssetException("IoTDevice", $"serial number '{command.SerialNumber}'");

        var apiKeyHash = ApiKeyHash.CreateFromPlainText(command.PlainTextApiKey);
        var device = technician is null ? IoTDevice.Register(serial, apiKeyHash, command.FirmwareVersion)
            : IoTDevice.RegisterForTechnician(serial, apiKeyHash, command.FirmwareVersion, technician);

        await _deviceRepository.AddAsync(device);
        await _unitOfWork.CompleteAsync();

        foreach (var evt in device.DomainEvents)
            await _publisher.Publish(evt);
        device.ClearDomainEvents();

        return device;
    }

    public async Task<IoTDevice> Handle(AssignDeviceToPropertyCommand command)
    {
        var device = await _deviceRepository.FindByIdAsync(IoTDeviceId.From(command.DeviceId))
            ?? throw new AssetNotFoundException("IoTDevice", command.DeviceId);

        var property = await _propertyRepository.FindByIdAsync(PropertyId.From(command.PropertyId))
            ?? throw new AssetNotFoundException("Property", command.PropertyId);

        device.AssignToProperty(property.Id, InstallationRequestId.From(command.InstallationRequestId));
        property.AddInstalledDevice(device.Id);

        await _unitOfWork.CompleteAsync();

        foreach (var evt in device.DomainEvents)
            await _publisher.Publish(evt);
        device.ClearDomainEvents();

        return device;
    }

    public async Task<IoTDevice> Handle(RecordDeviceInstallationCommand command)
    {
        var device = await _deviceRepository.FindByIdAsync(IoTDeviceId.From(command.DeviceId))
            ?? throw new AssetNotFoundException("IoTDevice", command.DeviceId);

        device.RecordInstallation(
            TechnicianId.From(command.TechnicianId),
            PropertyId.From(command.PropertyId),
            command.FirmwareVersion,
            command.InstalledAt);

        await _unitOfWork.CompleteAsync();

        foreach (var evt in device.DomainEvents)
            await _publisher.Publish(evt);
        device.ClearDomainEvents();

        return device;
    }

    public async Task<IoTDevice> Handle(UpdateDeviceConnectionStatusCommand command)
    {
        var device = await _deviceRepository.FindByIdAsync(IoTDeviceId.From(command.DeviceId))
            ?? throw new AssetNotFoundException("IoTDevice", command.DeviceId);

        device.UpdateConnectionStatus(command.NewConnectionStatus, command.LastReadingAt);

        await _unitOfWork.CompleteAsync();

        foreach (var evt in device.DomainEvents)
            await _publisher.Publish(evt);
        device.ClearDomainEvents();

        return device;
    }

    public async Task<IoTDevice> Handle(SendDeviceToMaintenanceCommand command)
    {
        var device = await _deviceRepository.FindByIdAsync(IoTDeviceId.From(command.DeviceId))
            ?? throw new AssetNotFoundException("IoTDevice", command.DeviceId);

        device.SendToMaintenance(command.Reason, command.ExpectedReturnDate);

        await _unitOfWork.CompleteAsync();

        foreach (var evt in device.DomainEvents)
            await _publisher.Publish(evt);
        device.ClearDomainEvents();

        return device;
    }

    public async Task<IoTDevice> Handle(ReinstallDeviceCommand command)
    {
        var device = await _deviceRepository.FindByIdAsync(IoTDeviceId.From(command.DeviceId))
            ?? throw new AssetNotFoundException("IoTDevice", command.DeviceId);

        device.Reinstall(
            TechnicianId.From(command.TechnicianId),
            command.FirmwareVersion,
            command.ReinstalledAt);

        await _unitOfWork.CompleteAsync();

        foreach (var evt in device.DomainEvents)
            await _publisher.Publish(evt);
        device.ClearDomainEvents();

        return device;
    }

    public async Task Handle(DecommissionOwnedDeviceCommand command)
    {
        var device = await InventoryDeviceAccess.FindOwned(command.Actor, command.DeviceId, _deviceRepository, _propertyRepository, _profiles);
        if (device == null) throw new DeviceRetirementUnavailableException();
        // Do not require Installed here: an authorized owner may repeat retirement idempotently.
        await Handle(new DecommissionDeviceCommand(command.DeviceId, command.Reason));
    }

    public async Task Handle(DecommissionDeviceCommand command)
    {
        var device = await _deviceRepository.FindByIdAsync(IoTDeviceId.From(command.DeviceId))
            ?? throw new AssetNotFoundException("IoTDevice", command.DeviceId);

        if (device.AssignedPropertyId is not null)
        {
            var property = await _propertyRepository.FindByIdAsync(device.AssignedPropertyId);
            property?.RemoveInstalledDevice(device.Id);
        }

        device.Decommission(command.Reason);

        await _unitOfWork.CompleteAsync();

        foreach (var evt in device.DomainEvents)
            await _publisher.Publish(evt);
        device.ClearDomainEvents();
    }
}
