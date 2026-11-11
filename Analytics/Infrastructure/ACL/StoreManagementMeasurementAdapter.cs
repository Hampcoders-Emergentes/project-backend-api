using Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Hampcoders.Electrolink.API.StoreManagement.Interfaces.ACL;

namespace Hampcoders.Electrolink.API.Analytics.Infrastructure.ACL;

/// <summary>Anti-corruption adapter: talks to StoreManagement only through its public ACL facade.</summary>
public sealed class StoreManagementMeasurementAdapter(IStoreMeasurementFacade facade) : IStoreMeasurementPort
{
    public async Task<StoreMeasurementSet> Load(UserId actor, string storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        try
        {
            var data = await facade.PowerSamples(actor, storeId, fromUtc, toUtc, ct);
            return new(data.EquipmentNames, data.Samples.Select(s => new PowerSample(s.EquipmentId, s.CapturedAtUtc, s.ActivePowerKw)).ToList());
        }
        catch (HierarchyNotFoundException)
        {
            throw new StoreNotAccessibleException();
        }
    }
}
