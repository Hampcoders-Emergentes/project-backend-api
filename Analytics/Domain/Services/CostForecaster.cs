namespace Hampcoders.Electrolink.API.Analytics.Domain.Services;

public sealed record CostForecast(string Method, decimal ProjectedCost, double ProjectedKwh, decimal LinearProjectedCost,
    decimal? MovingAverageProjectedCost, double ElapsedDays, double RemainingDays, int CompleteDaysUsed);

/// <summary>
///     Pure domain service: projects end-of-month spending from month-to-date consumption.
///     MOVING_AVERAGE_7D: month-to-date plus the average of the last (up to 7) complete days times the remaining days;
///     used once at least 3 complete days exist. LINEAR: month-to-date rate extended to the whole month.
/// </summary>
public static class CostForecaster
{
    public const int MovingAverageWindowDays = 7;
    public const int MinimumCompleteDays = 3;

    /// <param name="completeDays">Totals of the month's days that already ended, oldest first (today excluded).</param>
    public static CostForecast Project(double monthToDateKwh, decimal monthToDateCost, double elapsedDays, double daysInMonth,
        IReadOnlyList<(double Kwh, decimal Cost)> completeDays)
    {
        if (daysInMonth <= 0 || elapsedDays < 0 || elapsedDays > daysInMonth) throw new ArgumentException("Invalid month progress.");
        var remaining = daysInMonth - elapsedDays;
        // Avoid extrapolating from a few minutes of data: at least one hour of the month must have elapsed.
        var linearFactor = elapsedDays < 1.0 / 24 ? 0 : daysInMonth / elapsedDays;
        var linearCost = EnergyCostCalculator.Round(monthToDateCost * (decimal)linearFactor);
        var linearKwh = monthToDateKwh * linearFactor;
        if (completeDays.Count < MinimumCompleteDays)
            return new("LINEAR", linearCost, linearKwh, linearCost, null, elapsedDays, remaining, completeDays.Count);

        var window = completeDays.TakeLast(MovingAverageWindowDays).ToList();
        var averageCost = window.Average(d => d.Cost);
        var averageKwh = window.Average(d => d.Kwh);
        var movingCost = EnergyCostCalculator.Round(monthToDateCost + averageCost * (decimal)remaining);
        return new("MOVING_AVERAGE_7D", movingCost, monthToDateKwh + averageKwh * remaining, linearCost, movingCost,
            elapsedDays, remaining, window.Count);
    }
}
