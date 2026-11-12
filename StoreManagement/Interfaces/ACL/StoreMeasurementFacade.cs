using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;

namespace Hampcoders.Electrolink.API.StoreManagement.Interfaces.ACL;

public sealed class StoreMeasurementFacade(StoreHierarchyService hierarchy, IStoreDashboardReadRepository equipment,
    IEquipmentTelemetryRepository telemetry) : IStoreMeasurementFacade
{
    public async Task<StorePowerData> PowerSamples(UserId actor, string storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        await hierarchy.GetStore(actor, storeId, ct);
        var names = (await equipment.EquipmentStatus(actor, storeId, ct)).ToDictionary(e => e.EquipmentId, e => e.Name);
        return new(names, Map(await telemetry.PowerSamples(actor, storeId, fromUtc, toUtc, ct)));
    }

    public async Task<StorePowerData> EquipmentPowerSamples(UserId actor, string storeId, string areaId, string equipmentId,
        DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var item = await hierarchy.GetEquipment(actor, storeId, areaId, equipmentId, ct);
        var rows = await telemetry.PowerSamples(actor, storeId, fromUtc, toUtc, ct, equipmentId);
        return new(new Dictionary<string, string> { [item.Id] = item.Name }, Map(rows));
    }

    public async Task<StoreElectricalData> ElectricalSamples(UserId actor, string storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        await hierarchy.GetStore(actor, storeId, ct);
        var limits = (await equipment.LiveEquipment(actor, storeId, ct)).Select(e => new EquipmentElectricalLimits(e.EquipmentId, e.Name,
            e.Thresholds.NominalVoltage, e.Thresholds.MaxCurrentAmps, e.Thresholds.MinPowerFactor)).ToList();
        var rows = await telemetry.PowerSamples(actor, storeId, fromUtc, toUtc, ct);
        return new(limits, rows.Select(r => new EquipmentElectricalSample(r.EquipmentId, r.CapturedAtUtc, r.Voltage, r.Current, r.PowerFactor)).ToList());
    }

    // Same formula as the Processing reading model: kW = V * A / 1000 * power factor.
    private static List<EquipmentPowerSample> Map(List<PowerSampleRow> rows) => rows.Select(r => new EquipmentPowerSample(r.EquipmentId,
        r.CapturedAtUtc, (double)r.Voltage * r.Current / 1000.0 * r.PowerFactor)).ToList();
}
