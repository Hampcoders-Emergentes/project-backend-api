using System.Globalization;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Analytics.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Analytics.Application.Internal.CommandServices;

/// <param name="DayOfWeek">MONDAY..SUNDAY.</param>
/// <param name="OpenTime">HH:mm, store local time.</param>
/// <param name="CloseTime">HH:mm; earlier than OpenTime closes after midnight, equal means open 24 hours.</param>
public sealed record OpeningDayResource(string DayOfWeek, string OpenTime, string CloseTime);
public sealed record StoreOpeningHoursResource(string StoreId, string TimeZone, bool IsConfigured, IReadOnlyList<OpeningDayResource> Days);

public sealed record GetStoreOpeningHoursQuery(UserId Actor, string StoreId);
public sealed record SetStoreOpeningHoursCommand(UserId Actor, string StoreId, IReadOnlyList<OpeningDayResource> Days);
public sealed record SetStoreEnergyBudgetCommand(UserId Actor, string StoreId, int Year, int Month, double TargetKWh, double AlertThresholdPercentage);

public sealed record StoreEnergyBudgetResource(string StoreId, int Year, int Month, double TargetKWh, double AlertThresholdPercentage,
    DateTime UpdatedAtUtc);

/// <summary>Configuration used by energy analytics: store opening hours and monthly consumption budgets.</summary>
public sealed class EnergyPlanningCommandService(IStoreMeasurementPort stores, IEnergyPlanningRepository repository, IUtilityRateProvider rates)
{
    private static readonly string[] Days = ["MONDAY", "TUESDAY", "WEDNESDAY", "THURSDAY", "FRIDAY", "SATURDAY", "SUNDAY"];

    public async Task<StoreOpeningHoursResource> Handle(GetStoreOpeningHoursQuery query, CancellationToken ct)
    {
        await Authorize(query.Actor, query.StoreId, ct);
        return await Resource(query.StoreId, await repository.OpeningHours(query.StoreId, ct), ct);
    }

    /// <summary>Replaces the weekly opening hours; days not listed are closed.</summary>
    public async Task<StoreOpeningHoursResource> Handle(SetStoreOpeningHoursCommand command, CancellationToken ct)
    {
        await Authorize(command.Actor, command.StoreId, ct);
        var days = (command.Days ?? []).Select(d => StoreOpeningSchedule.Define(command.StoreId, Day(d.DayOfWeek), Time(d.OpenTime), Time(d.CloseTime))).ToList();
        if (days.GroupBy(d => d.DayOfWeek).Any(g => g.Count() > 1)) throw new ArgumentException("Each day of the week may appear only once.");
        await repository.ReplaceOpeningHours(command.StoreId, days, ct);
        return await Resource(command.StoreId, days, ct);
    }

    /// <summary>Creates or updates the budget of a month.</summary>
    public async Task<StoreEnergyBudgetResource> Handle(SetStoreEnergyBudgetCommand command, CancellationToken ct)
    {
        await Authorize(command.Actor, command.StoreId, ct);
        var now = DateTime.UtcNow;
        var budget = await repository.Budget(command.StoreId, command.Year, command.Month, ct);
        if (budget == null)
        {
            budget = StoreEnergyBudget.Define(command.StoreId, command.Year, command.Month, command.TargetKWh, command.AlertThresholdPercentage,
                command.Actor.Value, now);
            repository.Add(budget);
        }
        else budget.Update(command.TargetKWh, command.AlertThresholdPercentage, command.Actor.Value, now);
        await repository.Save(ct);
        return new(budget.StoreId, budget.Year, budget.Month, budget.TargetKWh, budget.AlertThresholdPercentage, budget.UpdatedAtUtc);
    }

    private async Task Authorize(UserId actor, string storeId, CancellationToken ct)
    {
        if (!await stores.CanAccess(actor, storeId, ct)) throw new StoreNotAccessibleException();
    }

    private async Task<StoreOpeningHoursResource> Resource(string storeId, IReadOnlyList<StoreOpeningSchedule> days, CancellationToken ct)
    {
        var tariff = await rates.ForStore(storeId, ct);
        return new(storeId, tariff.TimeZone.Id, days.Count > 0, days.OrderBy(d => ((int)d.DayOfWeek + 6) % 7)
            .Select(d => new OpeningDayResource(d.DayOfWeek.ToString().ToUpperInvariant(), d.OpenTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                d.CloseTime.ToString("HH:mm", CultureInfo.InvariantCulture))).ToList());
    }

    private static DayOfWeek Day(string value)
    {
        var day = (value ?? "").Trim().ToUpperInvariant();
        if (!Days.Contains(day)) throw new ArgumentException("dayOfWeek must be MONDAY..SUNDAY.");
        return Enum.Parse<DayOfWeek>(day, ignoreCase: true);
    }

    private static TimeOnly Time(string value) =>
        TimeOnly.TryParseExact(value ?? "", "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time : throw new ArgumentException("openTime and closeTime must use the HH:mm format.");
}
