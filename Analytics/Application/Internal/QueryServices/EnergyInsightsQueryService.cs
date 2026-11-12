using Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.Enums;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Analytics.Domain.Repositories;
using Hampcoders.Electrolink.API.Analytics.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Analytics.Application.Internal.QueryServices;

public sealed record CompareConsumptionPeriodsQuery(UserId Actor, string StoreId, DateTime Period1FromUtc, DateTime Period1ToUtc,
    DateTime Period2FromUtc, DateTime Period2ToUtc);
public sealed record GetOffHoursConsumptionQuery(UserId Actor, string StoreId, DateTime FromUtc, DateTime ToUtc, double StandbyThresholdKw);
public sealed record GetBudgetStatusQuery(UserId Actor, string StoreId, int? Year, int? Month);
public sealed record GetMaintenancePredictionsQuery(UserId Actor, string StoreId, int LookbackDays);

/// <param name="SampleCount">Power samples captured inside the period (all equipment).</param>
public sealed record PeriodConsumptionResource(DateTime FromUtc, DateTime ToUtc, double Days, double Kwh, decimal Cost, double AverageDailyKwh,
    int SampleCount);
/// <param name="DataQuality">INSUFFICIENT_DATA when this equipment has fewer than MinimumSamples samples in either period.</param>
/// <param name="SampleCount">Samples of this equipment in its less-observed period.</param>
public sealed record EquipmentComparisonResource(string EquipmentId, string EquipmentName, double Period1Kwh, double Period2Kwh,
    decimal Period1Cost, decimal Period2Cost, double? KwhChangePercent, double? CostChangePercent, string DataQuality, int SampleCount,
    int MinimumSamples);
/// <param name="KwhChangePercent">(period2 - period1) / period1; null when period 1 consumed nothing.</param>
/// <param name="AverageDailyKwhChangePercent">Same on the daily average, so periods of different length compare fairly.</param>
/// <param name="DataQuality">INSUFFICIENT_DATA when either period has fewer than <paramref name="MinimumSamples"/> samples: a zero
///     or a 100% change then reflects missing telemetry, not consumption.</param>
/// <param name="SampleCount">Samples of the period with fewer samples.</param>
/// <param name="MinimumSamples">Samples each period needs (energy is integrated between consecutive samples).</param>
public sealed record ConsumptionComparisonResource(string StoreId, string Currency, PeriodConsumptionResource Period1,
    PeriodConsumptionResource Period2, double KwhDifference, decimal CostDifference, double? KwhChangePercent, double? CostChangePercent,
    double? AverageDailyKwhChangePercent, IReadOnlyList<EquipmentComparisonResource> Equipment,
    string DataQuality, int SampleCount, int MinimumSamples);

public sealed record OffHoursEpisodeResource(string EquipmentId, string EquipmentName, DateTime StartUtc, DateTime EndUtc, double Kwh,
    decimal Cost, double PeakKw, double AverageKw, int SampleCount);
public sealed record OffHoursEquipmentResource(string EquipmentId, string EquipmentName, int Episodes, double Kwh, decimal Cost, double PeakKw);
public sealed record OffHoursConsumptionResource(string StoreId, DateTime FromUtc, DateTime ToUtc, string TimeZone, string Currency,
    double StandbyThresholdKw, double TotalKwh, decimal TotalCost, IReadOnlyList<OffHoursEquipmentResource> Equipment,
    IReadOnlyList<OffHoursEpisodeResource> Episodes);

/// <param name="Status">NO_BUDGET, ON_TRACK, AT_RISK, WARNING (alert threshold reached) or EXCEEDED.</param>
public sealed record BudgetStatusResource(string StoreId, int Year, int Month, string TimeZone, string Status, bool IsWarning,
    double? TargetKWh, double? AlertThresholdPercentage, double ConsumedKwh, double? ConsumedPercentage, double? RemainingKwh,
    double ProjectedKwh, double? ProjectedPercentage, DateTime MonthStartUtc, DateTime MonthEndUtc, DateTime GeneratedAtUtc);

public sealed record RepairWindowResource(DateTime StartUtc, DateTime EndUtc, string Reason);
public sealed record MaintenancePredictionResource(string EquipmentId, string EquipmentName, string Severity, int RunningSamples,
    double? BaselinePowerFactor, double? RecentPowerFactor, double? PowerFactorSlopePerDay, double? DaysToPowerFactorLimit,
    double? BaselineFluctuationIndex, double? RecentFluctuationIndex, IReadOnlyList<WearFinding> Findings, RepairWindowResource? RecommendedWindow,
    string DataQuality, int SampleCount, int MinimumSamples);
/// <param name="DataQuality">SUFFICIENT or INSUFFICIENT_DATA for one equipment.</param>
/// <param name="SampleCount">Running samples (equipment switched on) in the window.</param>
public sealed record EquipmentDataQualityResource(string EquipmentId, string EquipmentName, string DataQuality, int SampleCount,
    int MinimumSamples);

/// <param name="DataQuality">
///     SUFFICIENT only when the store has equipment and every one of them reached <paramref name="MinimumSamples"/>;
///     otherwise INSUFFICIENT_DATA, and an empty <paramref name="Predictions"/> list must not be read as "healthy".
/// </param>
/// <param name="SampleCount">Running samples of the least-observed equipment (0 when the store has no equipment).</param>
/// <param name="MinimumSamples">Running samples each equipment needs before it is analysed.</param>
/// <param name="EquipmentDataQuality">The same assessment per equipment.</param>
public sealed record MaintenancePredictionsResource(string StoreId, DateTime FromUtc, DateTime ToUtc, DateTime GeneratedAtUtc,
    int AnalysedEquipment, IReadOnlyList<string> InsufficientDataEquipmentIds, IReadOnlyList<MaintenancePredictionResource> Predictions,
    string DataQuality, int SampleCount, int MinimumSamples, IReadOnlyList<EquipmentDataQualityResource> EquipmentDataQuality);

/// <summary>Period comparison, off-hours consumption, budget tracking and predictive maintenance of a store.</summary>
public sealed class EnergyInsightsQueryService(IStoreMeasurementPort measurements, IUtilityRateProvider rates, IEnergyPlanningRepository planning)
{
    public const int MaxEpisodes = 200;

    public async Task<ConsumptionComparisonResource> Handle(CompareConsumptionPeriodsQuery query, CancellationToken ct)
    {
        var tariff = await rates.ForStore(query.StoreId, ct);
        var p1 = await Consumption(query.Actor, query.StoreId, query.Period1FromUtc, query.Period1ToUtc, tariff, ct);
        var p2 = await Consumption(query.Actor, query.StoreId, query.Period2FromUtc, query.Period2ToUtc, tariff, ct);
        var period1 = Period(query.Period1FromUtc, query.Period1ToUtc, p1.Values.Values, p1.SampleCount);
        var period2 = Period(query.Period2FromUtc, query.Period2ToUtc, p2.Values.Values, p2.SampleCount);
        var sampleCount = Math.Min(p1.SampleCount, p2.SampleCount);
        var names = p1.Names.Concat(p2.Names).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.First().Value);
        var equipment = names.Select(kv =>
        {
            var a = p1.Values.GetValueOrDefault(kv.Key);
            var b = p2.Values.GetValueOrDefault(kv.Key);
            var samples = Math.Min(p1.Counts.GetValueOrDefault(kv.Key), p2.Counts.GetValueOrDefault(kv.Key));
            return new EquipmentComparisonResource(kv.Key, kv.Value, Math.Round(a.Kwh, 6), Math.Round(b.Kwh, 6), EnergyCostCalculator.Round(a.Cost),
                EnergyCostCalculator.Round(b.Cost), ChangeRate.Percent(a.Kwh, b.Kwh), ChangeRate.Percent((double)a.Cost, (double)b.Cost),
                DataQuality.Evaluate(samples, EnergyIntegrator.MinimumSamples), samples, EnergyIntegrator.MinimumSamples);
        }).OrderByDescending(e => Math.Abs(e.Period2Kwh - e.Period1Kwh)).ThenBy(e => e.EquipmentName, StringComparer.Ordinal).ToList();
        return new(query.StoreId, tariff.Currency, period1, period2, Math.Round(period2.Kwh - period1.Kwh, 6), period2.Cost - period1.Cost,
            ChangeRate.Percent(period1.Kwh, period2.Kwh), ChangeRate.Percent((double)period1.Cost, (double)period2.Cost),
            ChangeRate.Percent(period1.AverageDailyKwh, period2.AverageDailyKwh), equipment,
            DataQuality.Evaluate(sampleCount, EnergyIntegrator.MinimumSamples), sampleCount, EnergyIntegrator.MinimumSamples);
    }

    public async Task<OffHoursConsumptionResource> Handle(GetOffHoursConsumptionQuery query, CancellationToken ct)
    {
        if (!double.IsFinite(query.StandbyThresholdKw) || query.StandbyThresholdKw is < 0 or > 1000)
            throw new ArgumentException("standbyThresholdKw must be between 0 and 1000.");
        if (!await measurements.CanAccess(query.Actor, query.StoreId, ct)) throw new StoreNotAccessibleException();
        var hours = new OpeningHoursCalendar(await planning.OpeningHours(query.StoreId, ct));
        if (!hours.IsConfigured)
            throw new ArgumentException("Opening hours are not configured for this store; set them with PUT analytics/opening-hours first.");
        var tariff = await rates.ForStore(query.StoreId, ct);
        var data = await measurements.Load(query.Actor, query.StoreId, query.FromUtc, query.ToUtc, ct);
        var episodes = OffHoursConsumptionDetector.Detect(data.Samples, hours, tariff, query.StandbyThresholdKw, query.FromUtc, query.ToUtc);
        string Name(string id) => data.EquipmentNames.GetValueOrDefault(id, id);
        var perEquipment = episodes.GroupBy(e => e.EquipmentId).Select(g => new OffHoursEquipmentResource(g.Key, Name(g.Key), g.Count(),
                Math.Round(g.Sum(e => e.Kwh), 6), g.Sum(e => e.Cost), g.Max(e => e.PeakKw)))
            .OrderByDescending(e => e.Kwh).ThenBy(e => e.EquipmentName, StringComparer.Ordinal).ToList();
        return new(query.StoreId, query.FromUtc, query.ToUtc, tariff.TimeZone.Id, tariff.Currency, query.StandbyThresholdKw,
            Math.Round(episodes.Sum(e => e.Kwh), 6), episodes.Sum(e => e.Cost), perEquipment,
            episodes.OrderByDescending(e => e.Kwh).Take(MaxEpisodes).Select(e => new OffHoursEpisodeResource(e.EquipmentId, Name(e.EquipmentId),
                e.StartUtc, e.EndUtc, e.Kwh, e.Cost, e.PeakKw, e.AverageKw, e.SampleCount)).ToList());
    }

    /// <summary>Month-to-date consumption against the month's budget (current month in the store's time zone by default).</summary>
    public async Task<BudgetStatusResource> Handle(GetBudgetStatusQuery query, CancellationToken ct)
    {
        if (!await measurements.CanAccess(query.Actor, query.StoreId, ct)) throw new StoreNotAccessibleException();
        var tariff = await rates.ForStore(query.StoreId, ct);
        var now = DateTime.UtcNow;
        var localNow = tariff.ToLocal(now);
        var year = query.Year ?? localNow.Year;
        var month = query.Month ?? localNow.Month;
        if (year is < 2000 or > 2100 || month is < 1 or > 12) throw new ArgumentException("year must be between 2000 and 2100 and month between 1 and 12.");
        var monthStartLocal = new DateTime(year, month, 1);
        var monthStartUtc = tariff.ToUtc(monthStartLocal);
        var monthEndUtc = tariff.ToUtc(monthStartLocal.AddMonths(1));
        var until = now < monthEndUtc ? now : monthEndUtc;
        var consumed = until <= monthStartUtc ? 0
            : (await Consumption(query.Actor, query.StoreId, monthStartUtc, until, tariff, ct)).Values.Values.Sum(v => v.Kwh);
        var elapsed = Math.Max(0, (until - monthStartUtc).TotalDays);
        var length = (monthEndUtc - monthStartUtc).TotalDays;
        var projected = elapsed <= 0 ? 0 : consumed / elapsed * length;
        var budget = await planning.Budget(query.StoreId, year, month, ct);
        if (budget == null)
            return new(query.StoreId, year, month, tariff.TimeZone.Id, "NO_BUDGET", false, null, null, Math.Round(consumed, 4), null, null,
                Math.Round(projected, 4), null, monthStartUtc, monthEndUtc, now);
        var status = BudgetStatus.Evaluate(consumed, projected, budget);
        return new(query.StoreId, year, month, tariff.TimeZone.Id, status, status is BudgetStatus.Warning or BudgetStatus.Exceeded,
            budget.TargetKWh, budget.AlertThresholdPercentage, Math.Round(consumed, 4), Math.Round(consumed / budget.TargetKWh * 100, 2),
            Math.Round(Math.Max(0, budget.TargetKWh - consumed), 4), Math.Round(projected, 4), Math.Round(projected / budget.TargetKWh * 100, 2),
            monthStartUtc, monthEndUtc, now);
    }

    public async Task<MaintenancePredictionsResource> Handle(GetMaintenancePredictionsQuery query, CancellationToken ct)
    {
        if (query.LookbackDays is < 3 or > 31) throw new ArgumentException("days must be between 3 and 31.");
        var now = DateTime.UtcNow;
        var from = now.AddDays(-query.LookbackDays);
        var data = await measurements.LoadElectrical(query.Actor, query.StoreId, from, now, ct);
        var tariff = await rates.ForStore(query.StoreId, ct);
        var hours = new OpeningHoursCalendar(await planning.OpeningHours(query.StoreId, ct));
        var window = RepairWindowPlanner.Next(now, tariff, hours);
        var bySample = data.Samples.GroupBy(s => s.EquipmentId).ToDictionary(g => g.Key, g => g.OrderBy(s => s.CapturedAtUtc).ToList());
        var insufficient = new List<string>();
        var predictions = new List<MaintenancePredictionResource>();
        var quality = new List<EquipmentDataQualityResource>();
        const int minimum = MaintenancePredictor.MinimumRunningSamples;
        foreach (var (equipmentId, limits) in data.Limits)
        {
            var samples = bySample.GetValueOrDefault(equipmentId) ?? [];
            var running = MaintenancePredictor.RunningSampleCount(samples, limits);
            quality.Add(new(equipmentId, data.EquipmentNames.GetValueOrDefault(equipmentId, equipmentId),
                DataQuality.Evaluate(running, minimum), running, minimum));
            var assessment = MaintenancePredictor.Assess(equipmentId, samples, limits, from, now);
            if (assessment == null) { insufficient.Add(equipmentId); continue; }
            if (assessment.Findings.Count == 0) continue;
            predictions.Add(new(equipmentId, data.EquipmentNames.GetValueOrDefault(equipmentId, equipmentId), assessment.Severity,
                assessment.RunningSamples, assessment.BaselinePowerFactor, assessment.RecentPowerFactor, assessment.PowerFactorSlopePerDay,
                assessment.DaysToPowerFactorLimit, assessment.BaselineFluctuationIndex, assessment.RecentFluctuationIndex, assessment.Findings,
                window == null ? null : new RepairWindowResource(window.Value.StartUtc, window.Value.EndUtc, window.Value.Reason),
                DataQuality.Sufficient, running, minimum));
        }
        var leastObserved = quality.Count == 0 ? 0 : quality.Min(q => q.SampleCount);
        var storeQuality = quality.Count > 0 && insufficient.Count == 0 ? DataQuality.Sufficient : DataQuality.InsufficientData;
        return new(query.StoreId, from, now, now, data.Limits.Count - insufficient.Count, insufficient,
            predictions.OrderByDescending(p => WearSeverity.Rank(p.Severity)).ThenBy(p => p.EquipmentName, StringComparer.Ordinal).ToList(),
            storeQuality, leastObserved, minimum,
            quality.OrderBy(q => q.EquipmentName, StringComparer.Ordinal).ToList());
    }

    private async Task<(IReadOnlyDictionary<string, string> Names, Dictionary<string, (double Kwh, decimal Cost)> Values, int SampleCount,
        Dictionary<string, int> Counts)> Consumption(
        UserId actor, string storeId, DateTime fromUtc, DateTime toUtc, UtilityRateSchedule tariff, CancellationToken ct)
    {
        var data = await measurements.Load(actor, storeId, fromUtc - EnergyIntegrator.DefaultMaxGap, toUtc, ct);
        var inPeriod = data.Samples.Where(s => s.CapturedAtUtc >= fromUtc && s.CapturedAtUtc < toUtc).ToList();
        var sampleCount = inPeriod.Count;
        var counts = inPeriod.GroupBy(s => s.EquipmentId).ToDictionary(g => g.Key, g => g.Count());
        var values = data.Samples.GroupBy(s => s.EquipmentId).ToDictionary(g => g.Key, g =>
        {
            var priced = EnergyCostCalculator.Price(EnergyIntegrator.Integrate(g.OrderBy(s => s.CapturedAtUtc).ToList(), fromUtc, toUtc,
                Granularity.Hourly), tariff);
            return (priced.Sum(b => b.Kwh), priced.Sum(b => b.Cost));
        });
        return (data.EquipmentNames, values, sampleCount, counts);
    }

    private static PeriodConsumptionResource Period(DateTime from, DateTime to, IReadOnlyCollection<(double Kwh, decimal Cost)> values,
        int sampleCount)
    {
        var kwh = values.Sum(v => v.Kwh);
        var days = (to - from).TotalDays;
        return new(from, to, Math.Round(days, 4), Math.Round(kwh, 6), EnergyCostCalculator.Round(values.Sum(v => v.Cost)), Math.Round(kwh / days, 6),
            sampleCount);
    }
}
