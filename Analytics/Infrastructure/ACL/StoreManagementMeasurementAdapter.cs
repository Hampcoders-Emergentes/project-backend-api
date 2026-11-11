using Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Analytics.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Hampcoders.Electrolink.API.StoreManagement.Interfaces.ACL;

namespace Hampcoders.Electrolink.API.Analytics.Infrastructure.ACL;

/// <summary>Anti-corruption adapter: talks to StoreManagement only through its public ACL facades.</summary>
public sealed class StoreManagementMeasurementAdapter(IStoreMeasurementFacade facade, IStoreAccessFacade access) : IStoreMeasurementPort
{
    public Task<StoreMeasurementSet> Load(UserId actor, string storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct) =>
        Translate(() => facade.PowerSamples(actor, storeId, fromUtc, toUtc, ct));

    public Task<StoreMeasurementSet> LoadEquipment(UserId actor, string storeId, string areaId, string equipmentId,
        DateTime fromUtc, DateTime toUtc, CancellationToken ct) =>
        Translate(() => facade.EquipmentPowerSamples(actor, storeId, areaId, equipmentId, fromUtc, toUtc, ct));

    public async Task<StoreElectricalSet> LoadElectrical(UserId actor, string storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        try
        {
            var data = await facade.ElectricalSamples(actor, storeId, fromUtc, toUtc, ct);
            return new(data.Equipment.ToDictionary(e => e.EquipmentId, e => e.Name),
                data.Equipment.ToDictionary(e => e.EquipmentId, e => new EquipmentOperatingLimits(e.NominalVoltage, e.MaxCurrentAmps, e.MinPowerFactor)),
                data.Samples.Select(s => new ElectricalSample(s.EquipmentId, s.CapturedAtUtc, s.Voltage, s.Current, s.PowerFactor)).ToList());
        }
        catch (HierarchyNotFoundException)
        {
            throw new StoreNotAccessibleException();
        }
    }

    public Task<bool> CanAccess(UserId actor, string storeId, CancellationToken ct) => access.CanManage(actor, storeId, ct);

    private static async Task<StoreMeasurementSet> Translate(Func<Task<StorePowerData>> load)
    {
        try
        {
            var data = await load();
            return new(data.EquipmentNames, data.Samples.Select(s => new PowerSample(s.EquipmentId, s.CapturedAtUtc, s.ActivePowerKw)).ToList());
        }
        catch (HierarchyNotFoundException)
        {
            throw new StoreNotAccessibleException();
        }
    }
}
