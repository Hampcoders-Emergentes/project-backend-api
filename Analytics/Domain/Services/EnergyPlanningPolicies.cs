using Hampcoders.Electrolink.API.Analytics.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Analytics.Domain.Services;

/// <summary>Weekly opening hours of a store, evaluated in local time.</summary>
public sealed class OpeningHoursCalendar(IEnumerable<StoreOpeningSchedule> days)
{
    private readonly IReadOnlyDictionary<DayOfWeek, StoreOpeningSchedule> _days = days.ToDictionary(d => d.DayOfWeek);

    public bool IsConfigured => _days.Count > 0;

    public bool IsOpen(DateTime local)
    {
        var time = TimeOnly.FromDateTime(local);
        if (_days.TryGetValue(local.DayOfWeek, out var today))
        {
            if (today.IsAroundTheClock) return true;
            if (!today.EndsNextDay ? time >= today.OpenTime && time < today.CloseTime : time >= today.OpenTime) return true;
        }
        // The previous day may close after midnight.
        return _days.TryGetValue(local.AddDays(-1).DayOfWeek, out var yesterday) && yesterday.EndsNextDay && time < yesterday.CloseTime;
    }
}

/// <summary>Monthly budget evaluation.</summary>
public static class BudgetStatus
{
    public const string OnTrack = "ON_TRACK";
    /// <summary>Not yet at the alert threshold, but the linear projection exceeds the target.</summary>
    public const string AtRisk = "AT_RISK";
    /// <summary>Consumption reached the alert threshold.</summary>
    public const string Warning = "WARNING";
    public const string Exceeded = "EXCEEDED";

    public static string Evaluate(double consumedKwh, double projectedKwh, StoreEnergyBudget budget)
    {
        var used = consumedKwh / budget.TargetKWh * 100;
        return used >= 100 ? Exceeded : used >= budget.AlertThresholdPercentage ? Warning : projectedKwh > budget.TargetKWh ? AtRisk : OnTrack;
    }
}

public static class ChangeRate
{
    /// <summary>Percentage change from baseline to current; null when the baseline is zero.</summary>
    public static double? Percent(double baseline, double current) =>
        Math.Abs(baseline) < 1e-9 ? null : Math.Round((current - baseline) / baseline * 100, 2);
}

/// <summary>Consumption above standby power while the store is closed.</summary>
public sealed record OffHoursEpisode(string EquipmentId, DateTime StartUtc, DateTime EndUtc, double Kwh, decimal Cost, double PeakKw,
    double AverageKw, int SampleCount);

/// <summary>
///     Domain service: finds equipment drawing more than the standby threshold while the store is closed. Consecutive
///     qualifying samples (no telemetry gap longer than <see cref="EnergyIntegrator.DefaultMaxGap"/>) form one episode;
///     its energy is integrated with the trapezoidal rule and priced at the tariff in force.
/// </summary>
public static class OffHoursConsumptionDetector
{
    public static IReadOnlyList<OffHoursEpisode> Detect(IReadOnlyList<PowerSample> orderedSamples, OpeningHoursCalendar hours,
        UtilityRateSchedule tariff, double standbyThresholdKw, DateTime fromUtc, DateTime toUtc)
    {
        var episodes = new List<OffHoursEpisode>();
        List<PowerSample>? current = null;
        void Close()
        {
            if (current is not { Count: > 0 }) { current = null; return; }
            double kwh = 0; decimal cost = 0;
            for (var i = 1; i < current.Count; i++)
            {
                var segment = (current[i - 1].ActivePowerKw + current[i].ActivePowerKw) / 2 * (current[i].CapturedAtUtc - current[i - 1].CapturedAtUtc).TotalHours;
                kwh += segment;
                cost += (decimal)segment * tariff.RateAt(current[i - 1].CapturedAtUtc);
            }
            episodes.Add(new(current[0].EquipmentId, current[0].CapturedAtUtc, current[^1].CapturedAtUtc, Math.Round(kwh, 6),
                EnergyCostCalculator.Round(cost), Math.Round(current.Max(s => s.ActivePowerKw), 4),
                Math.Round(current.Average(s => s.ActivePowerKw), 4), current.Count));
            current = null;
        }

        PowerSample? previous = null;
        foreach (var sample in orderedSamples)
        {
            if (sample.CapturedAtUtc < fromUtc || sample.CapturedAtUtc >= toUtc) continue;
            var qualifies = sample.ActivePowerKw > standbyThresholdKw && !hours.IsOpen(tariff.ToLocal(sample.CapturedAtUtc));
            var contiguous = previous != null && previous.EquipmentId == sample.EquipmentId
                             && sample.CapturedAtUtc - previous.CapturedAtUtc <= EnergyIntegrator.DefaultMaxGap;
            if (!qualifies || !contiguous) Close();
            if (qualifies) (current ??= []).Add(sample);
            previous = sample;
        }
        Close();
        return episodes;
    }
}
