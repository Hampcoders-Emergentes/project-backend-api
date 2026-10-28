using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Queries;

namespace Hampcoders.Electrolink.API.Assets.Domain.Services;

public interface IIoTDeviceQueryService
{
    Task<IoTDevice?> Handle(GetIoTDeviceByIdQuery query);
    Task<IoTDevice?> Handle(GetOwnedIoTDeviceByIdQuery query);
    Task<IoTDevice?> Handle(GetIoTDeviceBySerialNumberQuery query);
    Task<IEnumerable<IoTDevice>> Handle(GetAllIoTDevicesQuery query);
    Task<IEnumerable<IoTDevice>> Handle(GetIoTDevicesByPropertyIdQuery query);
    Task<IEnumerable<IoTDevice>> Handle(GetIoTDevicesByStatusQuery query);
    Task<int> Handle(GetActiveDeviceCountByOwnerIdQuery query);
}
