using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Queries;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
namespace Hampcoders.Electrolink.API.Assets.Application.Internal.CommandServices;

public sealed class TechnicianStockAccessDeniedException() : Exception("Technician stock is unavailable.");

public sealed class TechnicianDeviceStockService(IUserQueryService users, IProfilesContextFacade profiles,
    ITechnicianInventoryRepository inventories, IIoTDeviceRepository devices, IIoTDeviceCommandService commands)
{
    private async Task<TechnicianId> Resolve(UserId actor)
    {
        var user = await users.Handle(new GetUserByIdQuery(actor.Value));
        if (user is not { Status: EUserStatus.Active }) throw new TechnicianStockAccessDeniedException();
        var claims = await profiles.GetProfileClaimsAsync(actor.Value);
        if (claims is not { ProfileStatus: "Active", BusinessRole: "Technician", RoleSubjectId: not null } profile
            || await profiles.GetTechnicianIdByUserIdAsync(actor.Value) != profile.RoleSubjectId)
            throw new TechnicianStockAccessDeniedException();
        var technician = TechnicianId.From(profile.RoleSubjectId);
        if (await inventories.FindByTechnicianIdAsync(technician) is null) throw new TechnicianStockAccessDeniedException();
        return technician;
    }

    public async Task<IoTDevice> Register(UserId actor, string serial, string key, string firmware) =>
        await commands.Handle(new RegisterTechnicianIoTDeviceCommand(await Resolve(actor), serial, key, firmware));

    public async Task<TechnicianDeviceStockPage> GetStock(UserId actor, int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(page));
        return await devices.GetTechnicianStockPageAsync(await Resolve(actor), page, pageSize);
    }

    public async Task<IEnumerable<IoTDevice>> GetLegacyStock(UserId actor, EDeviceStatus? status = null) =>
        await devices.FindTechnicianStockAsync(await Resolve(actor), status);

    public async Task<IoTDevice?> GetDevice(UserId actor, string deviceId)
    {
        var technician = await Resolve(actor);
        var device = await devices.FindByIdAsync(IoTDeviceId.From(deviceId));
        return device?.StockTechnicianId == technician ? device : null;
    }

    public async Task<int?> GetLegacyActiveCount(UserId actor, string ownerId)
    {
        var technician = await Resolve(actor);
        if (technician.Value != ownerId) return null;
        return await devices.CountTechnicianActiveDevicesAsync(technician);
    }
}
