using Hampcoders.Electrolink.API.Analytics.Domain.Model.Enums;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Analytics.Domain.Services;

/// <summary>
///     Pure domain service: integrates power over time (trapezoidal rule) into kWh.
///     Consecutive samples further apart than <c>maxGap</c> are treated as a telemetry outage and contribute no energy
///     (the device may have been off or disconnected; extrapolating would invent consumption).
///     Energy is split exactly at bucket boundaries by linear interpolation of power.
/// </summary>
public static class EnergyIntegrator
{
    public static readonly TimeSpan DefaultMaxGap = TimeSpan.FromMinutes(15);

    public static IReadOnlyList<ConsumptionBucket> Integrate(IReadOnlyList<PowerSample> orderedSamples, DateTime fromUtc,
        DateTime toUtc, Granularity granularity, TimeSpan? maxGap = null)
    {
        var limit = maxGap ?? DefaultMaxGap;
        var buckets = new SortedDictionary<DateTime, double>();
        for (var i = 1; i < orderedSamples.Count; i++)
        {
            var a = orderedSamples[i - 1];
            var b = orderedSamples[i];
            var span = b.CapturedAtUtc - a.CapturedAtUtc;
            if (span <= TimeSpan.Zero || span > limit) continue;

            var start = a.CapturedAtUtc < fromUtc ? fromUtc : a.CapturedAtUtc;
            var end = b.CapturedAtUtc > toUtc ? toUtc : b.CapturedAtUtc;
            if (start >= end) continue;

            double PowerAt(DateTime t) => a.ActivePowerKw + (b.ActivePowerKw - a.ActivePowerKw) * ((t - a.CapturedAtUtc).TotalSeconds / span.TotalSeconds);

            var cursor = start;
            while (cursor < end)
            {
                var bucketStart = BucketStart(cursor, granularity);
                var next = Next(bucketStart, granularity);
                var segmentEnd = next < end ? next : end;
                var kwh = (PowerAt(cursor) + PowerAt(segmentEnd)) / 2.0 * (segmentEnd - cursor).TotalHours;
                buckets[bucketStart] = buckets.GetValueOrDefault(bucketStart) + kwh;
                cursor = segmentEnd;
            }
        }
        return buckets.Select(kv => new ConsumptionBucket(kv.Key, kv.Value)).ToList();
    }

    public static double Total(IEnumerable<ConsumptionBucket> buckets) => buckets.Sum(b => b.Kwh);

    private static DateTime BucketStart(DateTime t, Granularity g) => g == Granularity.Hourly
        ? new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0, DateTimeKind.Utc)
        : new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime Next(DateTime bucketStart, Granularity g) => g == Granularity.Hourly ? bucketStart.AddHours(1) : bucketStart.AddDays(1);
}
