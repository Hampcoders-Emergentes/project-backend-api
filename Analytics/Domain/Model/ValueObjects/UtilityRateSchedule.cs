namespace Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;

/// <summary>
///     Electricity tariff of a store: a base price per kWh and optional time-of-use factors.
///     During the peak window [PeakStartHour, PeakEndHour) in the tariff's time zone the price is
///     base * PeakRateFactor; outside it, base * OffPeakRateFactor. Without a window every hour costs base.
/// </summary>
public sealed record UtilityRateSchedule
{
    public string Currency { get; }
    public decimal BaseRatePerKwh { get; }
    public decimal PeakRateFactor { get; }
    public decimal OffPeakRateFactor { get; }
    public int? PeakStartHour { get; }
    public int? PeakEndHour { get; }
    public TimeZoneInfo TimeZone { get; }

    private UtilityRateSchedule(string currency, decimal baseRate, decimal peakFactor, decimal offPeakFactor, int? peakStart,
        int? peakEnd, TimeZoneInfo timeZone)
    {
        Currency = currency; BaseRatePerKwh = baseRate; PeakRateFactor = peakFactor; OffPeakRateFactor = offPeakFactor;
        PeakStartHour = peakStart; PeakEndHour = peakEnd; TimeZone = timeZone;
    }

    public static UtilityRateSchedule Create(string currency, decimal baseRatePerKwh, string timeZoneId, decimal peakRateFactor = 1m,
        decimal offPeakRateFactor = 1m, int? peakStartHour = null, int? peakEndHour = null)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3 || !currency.Trim().All(char.IsLetter))
            throw new ArgumentException("Currency must be a three-letter ISO code.");
        if (baseRatePerKwh <= 0 || baseRatePerKwh > 100) throw new ArgumentException("Base rate must be positive and at most 100 per kWh.");
        if (peakRateFactor <= 0 || peakRateFactor > 10 || offPeakRateFactor <= 0 || offPeakRateFactor > 10)
            throw new ArgumentException("Rate factors must be greater than 0 and at most 10.");
        if (peakStartHour.HasValue != peakEndHour.HasValue)
            throw new ArgumentException("Peak window requires both start and end hours.");
        if (peakStartHour is < 0 or > 23 || peakEndHour is < 0 or > 23 || (peakStartHour.HasValue && peakStartHour == peakEndHour))
            throw new ArgumentException("Peak hours must be distinct values between 0 and 23.");
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        { throw new ArgumentException($"Unknown time zone '{timeZoneId}'."); }
        return new(currency.Trim().ToUpperInvariant(), baseRatePerKwh, peakRateFactor, offPeakRateFactor, peakStartHour, peakEndHour, zone);
    }

    public bool HasTimeOfUse => PeakStartHour.HasValue;

    public DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone);

    public DateTime ToUtc(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZone);

    /// <summary>Whether the instant falls in the peak window (windows may wrap past midnight, e.g. 22 to 6).</summary>
    public bool IsPeak(DateTime utc) => IsPeakLocalHour(ToLocal(utc).Hour);

    public bool IsPeakLocalHour(int hour) => HasTimeOfUse && (PeakStartHour < PeakEndHour
        ? hour >= PeakStartHour && hour < PeakEndHour
        : hour >= PeakStartHour || hour < PeakEndHour);

    public decimal RateAt(DateTime utc) => BaseRatePerKwh * (IsPeak(utc) ? PeakRateFactor : HasTimeOfUse ? OffPeakRateFactor : 1m);
}
