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
        var rows = await telemetry.PowerSamples(actor, storeId, fromUtc, toUtc, ct);
        // Same formula as the Processing reading model: kW = V * A / 1000 * power factor.
        return new(names, rows.Select(r => new EquipmentPowerSample(r.EquipmentId, r.CapturedAtUtc,
            (double)r.Voltage * r.Current / 1000.0 * r.PowerFactor)).ToList());
    }
}
