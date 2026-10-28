using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;

namespace Hampcoders.Electrolink.API.Assets.Domain.Services;

public interface IIoTDeviceCommandService
{
    Task<IoTDevice> Handle(RegisterIoTDeviceCommand command);
    Task<IoTDevice> Handle(RegisterTechnicianIoTDeviceCommand command);
    Task<IoTDevice> Handle(AssignDeviceToPropertyCommand command);
    Task<IoTDevice> Handle(RecordDeviceInstallationCommand command);
    Task<IoTDevice> Handle(UpdateDeviceConnectionStatusCommand command);
    Task<IoTDevice> Handle(SendDeviceToMaintenanceCommand command);
    Task<IoTDevice> Handle(ReinstallDeviceCommand command);
    Task Handle(DecommissionDeviceCommand command);
    Task Handle(DecommissionOwnedDeviceCommand command);
}
