using Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.Enums;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Analytics.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Analytics.Application.Internal.QueryServices;

public sealed record GetEquipmentPowerConsumptionQuery(UserId Actor, string StoreId, string AreaId, string EquipmentId,
    DateTime FromUtc, DateTime ToUtc, Granularity Granularity);
public sealed record GetStoreCostForecastQuery(UserId Actor, string StoreId);

public sealed record CostResource(string Currency, decimal BaseRatePerKwh, decimal TotalCost, double PeakKwh, double OffPeakKwh,
    decimal PeakCost, decimal OffPeakCost);
/// <param name="StartUtc">Bucket start; daily buckets start at local midnight of the tariff time zone.</param>
public sealed record CostBucketResource(DateTime StartUtc, double Kwh, decimal Cost);
public sealed record EquipmentPowerConsumptionResource(string StoreId, string EquipmentId, string EquipmentName, DateTime FromUtc,
    DateTime ToUtc, string Granularity, string TimeZone, double Kwh, double PeakKw, double AverageKw, int SampleCount,
    CostResource Cost, IReadOnlyList<CostBucketResource> Series);

public sealed record DailyCostResource(DateOnly Date, double Kwh, decimal Cost);
public sealed record EquipmentCostShareResource(string EquipmentId, string EquipmentName, double MonthToDateKwh, decimal MonthToDateCost,
    decimal ProjectedCost);
/// <param name="Method">MOVING_AVERAGE_7D (at least 3 complete days) or LINEAR.</param>
public sealed record StoreCostForecastResource(string StoreId, string Currency, string TimeZone, DateTime MonthStartUtc,
    DateTime MonthEndUtc, DateTime GeneratedAtUtc, double MonthToDateKwh, decimal MonthToDateCost, double ElapsedDays,
    double DaysInMonth, string Method, double ProjectedKwh, decimal ProjectedCost, decimal LinearProjectedCost,
    decimal? MovingAverageProjectedCost, IReadOnlyList<DailyCostResource> Daily, IReadOnlyList<EquipmentCostShareResource> Equipment);

/// <summary>Energy and cost per equipment, and the end-of-month cost projection of a store.</summary>
public sealed class EnergyCostQueryService(IStoreMeasurementPort measurements, IUtilityRateProvider rates)
{
    public async Task<EquipmentPowerConsumptionResource> Handle(GetEquipmentPowerConsumptionQuery query, CancellationToken ct)
    {
        var data = await measurements.LoadEquipment(query.Actor, query.StoreId, query.AreaId, query.EquipmentId,
            query.FromUtc - EnergyIntegrator.DefaultMaxGap, query.ToUtc, ct);
        var tariff = await rates.ForStore(query.StoreId, ct);
        var samples = data.Samples.Where(s => s.EquipmentId == query.EquipmentId).OrderBy(s => s.CapturedAtUtc).ToList();
        var priced = EnergyCostCalculator.Price(EnergyIntegrator.Integrate(samples, query.FromUtc, query.ToUtc, Granularity.Hourly), tariff);
        var summary = EnergyCostCalculator.Summarize(priced, tariff);
        var inWindow = samples.Where(s => s.CapturedAtUtc >= query.FromUtc && s.CapturedAtUtc < query.ToUtc).ToList();
        var series = query.Granularity == Granularity.Hourly
            ? priced.Select(b => new CostBucketResource(b.StartUtc, Math.Round(b.Kwh, 6), EnergyCostCalculator.Round(b.Cost))).ToList()
            : EnergyCostCalculator.Daily(priced, tariff).Select(d => new CostBucketResource(
                tariff.ToUtc(d.Day.ToDateTime(TimeOnly.MinValue)), Math.Round(d.Kwh, 6), EnergyCostCalculator.Round(d.Cost))).ToList();
        return new(query.StoreId, query.EquipmentId, data.EquipmentNames.GetValueOrDefault(query.EquipmentId, query.EquipmentId),
            query.FromUtc, query.ToUtc, query.Granularity.ToString().ToLowerInvariant(), tariff.TimeZone.Id, Math.Round(summary.TotalKwh, 6),
            inWindow.Count == 0 ? 0 : Math.Round(inWindow.Max(s => s.ActivePowerKw), 4),
            Math.Round(summary.TotalKwh / (query.ToUtc - query.FromUtc).TotalHours, 4), inWindow.Count, Cost(summary, tariff), series);
    }

    public async Task<StoreCostForecastResource> Handle(GetStoreCostForecastQuery query, CancellationToken ct)
    {
        var tariff = await rates.ForStore(query.StoreId, ct);
        var nowUtc = DateTime.UtcNow;
        var localNow = tariff.ToLocal(nowUtc);
        var monthStartLocal = new DateTime(localNow.Year, localNow.Month, 1);
        var monthStartUtc = tariff.ToUtc(monthStartLocal);
        var monthEndUtc = tariff.ToUtc(monthStartLocal.AddMonths(1));
        var data = await measurements.Load(query.Actor, query.StoreId, monthStartUtc - EnergyIntegrator.DefaultMaxGap, nowUtc, ct);

        var perEquipment = data.Samples.GroupBy(s => s.EquipmentId).Select(g => (EquipmentId: g.Key, Priced: EnergyCostCalculator.Price(
            EnergyIntegrator.Integrate(g.OrderBy(s => s.CapturedAtUtc).ToList(), monthStartUtc, nowUtc, Granularity.Hourly), tariff))).ToList();
        var storeHourly = perEquipment.SelectMany(e => e.Priced).GroupBy(b => b.StartUtc).OrderBy(g => g.Key)
            .Select(g => new PricedBucket(g.Key, g.Sum(b => b.Kwh), g.Sum(b => b.Cost), g.First().IsPeak)).ToList();
        var daily = EnergyCostCalculator.Daily(storeHourly, tariff);
        var today = DateOnly.FromDateTime(localNow);
        // Days with no data still count as complete days with zero consumption (the store may have been closed).
        var completeDays = Enumerable.Range(0, today.Day - 1).Select(i => DateOnly.FromDateTime(monthStartLocal).AddDays(i))
            .Select(day => daily.FirstOrDefault(d => d.Day == day)).Select(d => (d.Kwh, d.Cost)).ToList();
        var mtdKwh = storeHourly.Sum(b => b.Kwh);
        var mtdCost = storeHourly.Sum(b => b.Cost);
        var daysInMonth = (monthEndUtc - monthStartUtc).TotalDays;
        var forecast = CostForecaster.Project(mtdKwh, mtdCost, (nowUtc - monthStartUtc).TotalDays, daysInMonth, completeDays);
        var shareFactor = mtdCost == 0 ? 0 : forecast.ProjectedCost / mtdCost;

        var equipment = data.EquipmentNames.Select(kv =>
        {
            var priced = perEquipment.FirstOrDefault(e => e.EquipmentId == kv.Key).Priced ?? [];
            var cost = priced.Sum(b => b.Cost);
            return new EquipmentCostShareResource(kv.Key, kv.Value, Math.Round(priced.Sum(b => b.Kwh), 6), EnergyCostCalculator.Round(cost),
                EnergyCostCalculator.Round(cost * shareFactor));
        }).OrderByDescending(e => e.MonthToDateCost).ThenBy(e => e.EquipmentName, StringComparer.Ordinal).ToList();

        return new(query.StoreId, tariff.Currency, tariff.TimeZone.Id, monthStartUtc, monthEndUtc, nowUtc, Math.Round(mtdKwh, 6),
            EnergyCostCalculator.Round(mtdCost), Math.Round(forecast.ElapsedDays, 4), Math.Round(daysInMonth, 4), forecast.Method,
            Math.Round(forecast.ProjectedKwh, 4), forecast.ProjectedCost, forecast.LinearProjectedCost, forecast.MovingAverageProjectedCost,
            daily.Select(d => new DailyCostResource(d.Day, Math.Round(d.Kwh, 6), EnergyCostCalculator.Round(d.Cost))).ToList(), equipment);
    }

    private static CostResource Cost(EnergyCostBreakdown s, UtilityRateSchedule tariff) => new(s.Currency, tariff.BaseRatePerKwh,
        s.TotalCost, Math.Round(s.PeakKwh, 6), Math.Round(s.OffPeakKwh, 6), s.PeakCost, s.OffPeakCost);
}
