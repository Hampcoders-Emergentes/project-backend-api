using Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Analytics.Domain.Services;

/// <summary>One electrical sample of an equipment, as needed for wear analysis.</summary>
public sealed record ElectricalSample(string EquipmentId, DateTime CapturedAtUtc, double Voltage, double Current, double PowerFactor);

/// <summary>Operating limits of an equipment that the wear analysis compares against.</summary>
public sealed record EquipmentOperatingLimits(double NominalVoltage, double MaxCurrentAmps, double MinPowerFactor);

public static class WearSeverity
{
    public const string Low = "LOW";
    public const string Medium = "MEDIUM";
    public const string High = "HIGH";
    public static int Rank(string severity) => severity switch { High => 3, Medium => 2, Low => 1, _ => 0 };
}

/// <param name="Code">POWER_FACTOR_DEGRADATION, CURRENT_FLUCTUATION, RECURRENT_OVERLOAD or SUPPLY_VOLTAGE_INSTABILITY.</param>
public sealed record WearFinding(string Code, string Severity, string Description, string RecommendedAction);

/// <param name="DaysToPowerFactorLimit">Projected days until the power factor trend crosses the configured minimum.</param>
public sealed record WearAssessment(string EquipmentId, int RunningSamples, double? BaselinePowerFactor, double? RecentPowerFactor,
    double? PowerFactorSlopePerDay, double? DaysToPowerFactorLimit, double? BaselineFluctuationIndex, double? RecentFluctuationIndex,
    IReadOnlyList<WearFinding> Findings)
{
    public string Severity => Findings.Count == 0 ? "NONE" : Findings.MaxBy(f => WearSeverity.Rank(f.Severity))!.Severity;
}

/// <summary>
///     Domain service: detects wear patterns in an equipment's electrical history. The window is split into a baseline
///     (first half) and a recent half; only samples where the equipment is running are analysed.
///     <list type="bullet">
///         <item>Power factor degradation: falling daily power factor (motor windings, capacitors).</item>
///         <item>Current fluctuation: rapid sample-to-sample current swings that grow over time, typical of mechanical
///         vibration (worn bearings, unbalanced fans) or loose connections.</item>
///         <item>Recurrent overload: frequent current above the configured maximum.</item>
///         <item>Supply voltage instability: frequent voltage outside ±10% of nominal at the equipment.</item>
///     </list>
/// </summary>
public static class MaintenancePredictor
{
    public const int MinimumRunningSamples = 30;
    private static readonly TimeSpan MaxPairGap = TimeSpan.FromMinutes(2);

    public static WearAssessment? Assess(string equipmentId, IReadOnlyList<ElectricalSample> ordered, EquipmentOperatingLimits limits,
        DateTime fromUtc, DateTime toUtc)
    {
        var runningFloor = Math.Max(0.2, limits.MaxCurrentAmps * 0.05);
        var running = ordered.Where(s => s.Current > runningFloor && double.IsFinite(s.PowerFactor)).ToList();
        if (running.Count < MinimumRunningSamples) return null;
        var middle = fromUtc + (toUtc - fromUtc) / 2;
        var baseline = running.Where(s => s.CapturedAtUtc < middle).ToList();
        var recent = running.Where(s => s.CapturedAtUtc >= middle).ToList();
        var findings = new List<WearFinding>();

        // Power factor trend (daily means, least squares).
        var daily = running.GroupBy(s => s.CapturedAtUtc.Date).OrderBy(g => g.Key)
            .Select(g => (Day: (g.Key - fromUtc.Date).TotalDays, Pf: g.Average(s => s.PowerFactor))).ToList();
        double? slope = daily.Count >= 3 ? Slope(daily) : null;
        double? basePf = baseline.Count > 0 ? baseline.Average(s => s.PowerFactor) : null;
        double? recentPf = recent.Count > 0 ? recent.Average(s => s.PowerFactor) : null;
        double? daysToLimit = null;
        if (slope is < 0 && recentPf is { } rp && rp > limits.MinPowerFactor)
            daysToLimit = Math.Round((rp - limits.MinPowerFactor) / -slope.Value, 1);
        if (recentPf is { } pf && basePf is { } bpf)
        {
            var drop = bpf - pf;
            if (pf < limits.MinPowerFactor || (drop >= 0.05 && slope < 0))
            {
                var severity = pf < limits.MinPowerFactor - 0.05 || daysToLimit is <= 7 ? WearSeverity.High
                    : pf < limits.MinPowerFactor || daysToLimit is <= 30 ? WearSeverity.Medium : WearSeverity.Low;
                findings.Add(new("POWER_FACTOR_DEGRADATION", severity,
                    $"Power factor fell from {bpf:0.00} to {pf:0.00} (minimum {limits.MinPowerFactor:0.00})" +
                    (daysToLimit is { } d ? $"; at this rate it reaches the minimum in about {d:0} day(s)." : "."),
                    "Inspect motor windings, run capacitors and compressor; check for overheating."));
            }
        }

        // Current fluctuation index: median relative change between consecutive running samples.
        var baseIndex = FluctuationIndex(baseline);
        var recentIndex = FluctuationIndex(recent);
        if (recentIndex is { } ri && ri >= 0.15 && (baseIndex == null || ri >= baseIndex * 1.5))
            findings.Add(new("CURRENT_FLUCTUATION", ri >= 0.3 ? WearSeverity.High : WearSeverity.Medium,
                $"Current swings by a median {ri * 100:0}% between readings" + (baseIndex is { } bi ? $" (was {bi * 100:0}%)." : "."),
                "Check bearings, fans and belts for vibration and tighten terminal connections."));

        // Recurrent overload in the recent half.
        if (recent.Count > 0)
        {
            var overload = recent.Count(s => s.Current > limits.MaxCurrentAmps) / (double)recent.Count;
            if (overload >= 0.05)
                findings.Add(new("RECURRENT_OVERLOAD", overload >= 0.2 ? WearSeverity.High : WearSeverity.Medium,
                    $"{overload * 100:0}% of recent running readings exceed {limits.MaxCurrentAmps:0.##} A.",
                    "Check for blocked airflow, worn heating elements or a seizing motor; verify the breaker rating."));

            var outOfRange = recent.Count(s => s.Voltage > limits.NominalVoltage * 1.1 || s.Voltage < limits.NominalVoltage * 0.9) / (double)recent.Count;
            if (outOfRange >= 0.1)
                findings.Add(new("SUPPLY_VOLTAGE_INSTABILITY", outOfRange >= 0.3 ? WearSeverity.Medium : WearSeverity.Low,
                    $"{outOfRange * 100:0}% of recent readings are outside ±10% of {limits.NominalVoltage:0} V.",
                    "Inspect the supply circuit, connections and neutral of the distribution board."));
        }

        return new(equipmentId, running.Count, Round(basePf), Round(recentPf), slope == null ? null : Math.Round(slope.Value, 5), daysToLimit,
            Round(baseIndex), Round(recentIndex), findings);
    }

    private static double? FluctuationIndex(IReadOnlyList<ElectricalSample> samples)
    {
        var changes = new List<double>();
        for (var i = 1; i < samples.Count; i++)
        {
            var a = samples[i - 1]; var b = samples[i];
            if (b.CapturedAtUtc - a.CapturedAtUtc > MaxPairGap) continue;
            var mean = (a.Current + b.Current) / 2;
            if (mean > 0) changes.Add(Math.Abs(b.Current - a.Current) / mean);
        }
        if (changes.Count < 10) return null;
        changes.Sort();
        return changes[changes.Count / 2];
    }

    private static double Slope(IReadOnlyList<(double X, double Y)> points)
    {
        var mx = points.Average(p => p.X); var my = points.Average(p => p.Y);
        var den = points.Sum(p => (p.X - mx) * (p.X - mx));
        return den == 0 ? 0 : points.Sum(p => (p.X - mx) * (p.Y - my)) / den;
    }

    private static double? Round(double? value) => value == null ? null : Math.Round(value.Value, 4);
}

/// <summary>
///     Domain service: earliest repair window that disturbs the store least: closed (per opening hours, when configured)
///     and outside the tariff's peak window. Searches whole local hours over the next days.
/// </summary>
public static class RepairWindowPlanner
{
    public static (DateTime StartUtc, DateTime EndUtc, string Reason)? Next(DateTime nowUtc, UtilityRateSchedule tariff,
        OpeningHoursCalendar hours, int hoursNeeded = 2, int searchDays = 7)
    {
        if (!hours.IsConfigured && !tariff.HasTimeOfUse) return null;
        var local = tariff.ToLocal(nowUtc);
        var cursor = new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0).AddHours(1);
        var end = cursor.AddDays(searchDays);
        DateTime? start = null; var run = 0;
        for (; cursor < end; cursor = cursor.AddHours(1))
        {
            var suitable = (!hours.IsConfigured || !hours.IsOpen(cursor)) && !tariff.IsPeakLocalHour(cursor.Hour);
            if (!suitable) { start = null; run = 0; continue; }
            start ??= cursor;
            if (++run < hoursNeeded) continue;
            var reason = hours.IsConfigured && tariff.HasTimeOfUse ? "Store closed and off-peak tariff."
                : hours.IsConfigured ? "Store closed." : "Off-peak tariff.";
            return (tariff.ToUtc(start.Value), tariff.ToUtc(start.Value.AddHours(hoursNeeded)), reason);
        }
        return null;
    }
}
