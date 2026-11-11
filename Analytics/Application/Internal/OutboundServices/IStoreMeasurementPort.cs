using Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;

public sealed record StoreMeasurementSet(IReadOnlyDictionary<string, string> EquipmentNames, IReadOnlyList<PowerSample> Samples);

/// <summary>The store is unknown or not owned by the actor (indistinguishable on purpose).</summary>
public sealed class StoreNotAccessibleException() : Exception("Store not found.");

/// <summary>
///     Outbound port to the store-monitoring context. Samples must be ordered by equipment then capture time
///     and limited to the actor's own store.
/// </summary>
public interface IStoreMeasurementPort
{
    Task<StoreMeasurementSet> Load(UserId actor, string storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);
}
