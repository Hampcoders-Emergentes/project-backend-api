using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Application;
namespace Hampcoders.Electrolink.API.AlertManagement.Infrastructure;

/// <summary>Adapter implementing the StoreManagement port with AlertManagement data.</summary>
public sealed class StoreAlertSummaryAdapter(IAlertRepository repository) : IStoreAlertSummaryPort
{
    public async Task<IReadOnlyDictionary<string, EquipmentAlertCounts>> OpenAlertCountsByEquipment(UserId actor, string storeId, CancellationToken ct) =>
        (await repository.OpenCountsByEquipment(actor, storeId, ct)).ToDictionary(kv => kv.Key, kv => new EquipmentAlertCounts(kv.Value.Critical, kv.Value.NonCritical));
}
