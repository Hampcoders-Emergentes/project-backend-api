namespace Hampcoders.Electrolink.API.Analytics.Domain.Model.Aggregates;

/// <summary>
///     Opening hours of a store for one day of the week, in the store's local time (tariff time zone).
///     CloseTime earlier than OpenTime means the store closes after midnight; equal times mean open around the clock.
///     A day without a row is a closed day.
/// </summary>
public sealed class StoreOpeningSchedule
{
    public string Id { get; private set; } = null!;
    public string StoreId { get; private set; } = null!;
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeOnly OpenTime { get; private set; }
    public TimeOnly CloseTime { get; private set; }
    private StoreOpeningSchedule() { }

    public static StoreOpeningSchedule Define(string storeId, DayOfWeek day, TimeOnly openTime, TimeOnly closeTime)
    {
        if (!Enum.IsDefined(day)) throw new ArgumentException("Invalid day of week.");
        if (openTime.Second != 0 || closeTime.Second != 0 || openTime.Millisecond != 0 || closeTime.Millisecond != 0)
            throw new ArgumentException("Opening hours use whole minutes (HH:mm).");
        return new() { Id = "hours-" + Guid.NewGuid().ToString("N"), StoreId = storeId, DayOfWeek = day, OpenTime = openTime, CloseTime = closeTime };
    }

    public bool IsAroundTheClock => OpenTime == CloseTime;
    public bool EndsNextDay => CloseTime < OpenTime;
}

/// <summary>Monthly energy ceiling of a store; a warning is raised once consumption reaches the alert threshold.</summary>
public sealed class StoreEnergyBudget
{
    public string Id { get; private set; } = null!;
    public string StoreId { get; private set; } = null!;
    public int Year { get; private set; }
    public int Month { get; private set; }
    public double TargetKWh { get; private set; }
    /// <summary>Share of the target (1-100 %) from which the budget is reported as nearing exhaustion.</summary>
    public double AlertThresholdPercentage { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public string UpdatedByUserId { get; private set; } = null!;
    private StoreEnergyBudget() { }

    public static StoreEnergyBudget Define(string storeId, int year, int month, double targetKwh, double alertThresholdPercentage,
        string actor, DateTime nowUtc)
    {
        var budget = new StoreEnergyBudget { Id = "budget-" + Guid.NewGuid().ToString("N"), StoreId = storeId };
        budget.Change(year, month, targetKwh, alertThresholdPercentage, actor, nowUtc);
        return budget;
    }

    public void Update(double targetKwh, double alertThresholdPercentage, string actor, DateTime nowUtc) =>
        Change(Year, Month, targetKwh, alertThresholdPercentage, actor, nowUtc);

    private void Change(int year, int month, double targetKwh, double threshold, string actor, DateTime nowUtc)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12) throw new ArgumentException("year must be between 2000 and 2100 and month between 1 and 12.");
        if (!double.IsFinite(targetKwh) || targetKwh <= 0 || targetKwh > 100_000_000) throw new ArgumentException("targetKWh must be positive.");
        if (!double.IsFinite(threshold) || threshold is < 1 or > 100) throw new ArgumentException("alertThresholdPercentage must be between 1 and 100.");
        Year = year; Month = month; TargetKWh = targetKwh; AlertThresholdPercentage = threshold; UpdatedByUserId = actor; UpdatedAtUtc = nowUtc;
    }
}
