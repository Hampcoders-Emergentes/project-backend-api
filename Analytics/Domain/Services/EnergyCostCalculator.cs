using Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Analytics.Domain.Services;

public sealed record PricedBucket(DateTime StartUtc, double Kwh, decimal Cost, bool IsPeak);

public sealed record EnergyCostBreakdown(string Currency, double TotalKwh, double PeakKwh, double OffPeakKwh,
    decimal PeakCost, decimal OffPeakCost, decimal TotalCost);

/// <summary>
///     Pure domain service: prices energy. Input buckets must be hourly (see <see cref="EnergyIntegrator"/>), which
///     integrates P = V * I * PF over each interval Δt with the trapezoidal rule; each hour is priced at the tariff
///     rate in force at its start. Hour alignment is exact for time zones with whole-hour offsets.
/// </summary>
public static class EnergyCostCalculator
{
    public static IReadOnlyList<PricedBucket> Price(IEnumerable<ConsumptionBucket> hourlyBuckets, UtilityRateSchedule rates) =>
        hourlyBuckets.Select(b => new PricedBucket(b.StartUtc, b.Kwh, (decimal)b.Kwh * rates.RateAt(b.StartUtc), rates.IsPeak(b.StartUtc)))
            .ToList();

    public static EnergyCostBreakdown Summarize(IReadOnlyCollection<PricedBucket> priced, UtilityRateSchedule rates)
    {
        var peak = priced.Where(b => b.IsPeak).ToList();
        var offPeak = priced.Where(b => !b.IsPeak).ToList();
        return new(rates.Currency, priced.Sum(b => b.Kwh), peak.Sum(b => b.Kwh), offPeak.Sum(b => b.Kwh),
            Round(peak.Sum(b => b.Cost)), Round(offPeak.Sum(b => b.Cost)), Round(priced.Sum(b => b.Cost)));
    }

    /// <summary>Groups hourly priced buckets by calendar day in the tariff's time zone.</summary>
    public static IReadOnlyList<(DateOnly Day, double Kwh, decimal Cost)> Daily(IEnumerable<PricedBucket> priced, UtilityRateSchedule rates) =>
        priced.GroupBy(b => DateOnly.FromDateTime(rates.ToLocal(b.StartUtc))).OrderBy(g => g.Key)
            .Select(g => (g.Key, g.Sum(b => b.Kwh), g.Sum(b => b.Cost))).ToList();

    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
