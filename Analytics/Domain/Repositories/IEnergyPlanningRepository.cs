using Hampcoders.Electrolink.API.Analytics.Domain.Model.Aggregates;

namespace Hampcoders.Electrolink.API.Analytics.Domain.Repositories;

/// <summary>Opening hours and monthly budgets of stores; callers authorize the store first.</summary>
public interface IEnergyPlanningRepository
{
    Task<List<StoreOpeningSchedule>> OpeningHours(string storeId, CancellationToken ct);
    /// <summary>Atomically replaces the store's weekly opening hours.</summary>
    Task ReplaceOpeningHours(string storeId, IReadOnlyList<StoreOpeningSchedule> days, CancellationToken ct);
    /// <summary>Budget of the month (tracked), or null.</summary>
    Task<StoreEnergyBudget?> Budget(string storeId, int year, int month, CancellationToken ct);
    void Add(StoreEnergyBudget budget);
    Task Save(CancellationToken ct);
}
