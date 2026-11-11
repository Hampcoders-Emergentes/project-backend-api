using System.Globalization;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Analytics.Infrastructure.Tariffs;

/// <summary>
///     Tariff from configuration: "Analytics:UtilityRate" holds the default, and "Analytics:UtilityRate:Stores:{storeId}"
///     may override any of its keys for one store. Keys: Currency, BaseRatePerKwh, TimeZone, PeakRateFactor,
///     OffPeakRateFactor, PeakStartHour, PeakEndHour.
/// </summary>
public sealed class ConfigurationUtilityRateProvider(IConfiguration configuration) : IUtilityRateProvider
{
    public Task<UtilityRateSchedule> ForStore(string storeId, CancellationToken ct)
    {
        var defaults = configuration.GetSection("Analytics:UtilityRate");
        var store = defaults.GetSection("Stores").GetSection(storeId ?? "");
        string? Value(string key) => store[key] ?? defaults[key];
        decimal Decimal(string key, decimal fallback) =>
            decimal.TryParse(Value(key), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        int? Hour(string key) => int.TryParse(Value(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

        return Task.FromResult(UtilityRateSchedule.Create(
            Value("Currency") ?? "PEN",
            Decimal("BaseRatePerKwh", 0.80m),
            Value("TimeZone") ?? "America/Lima",
            Decimal("PeakRateFactor", 1m),
            Decimal("OffPeakRateFactor", 1m),
            Hour("PeakStartHour") ?? (Value("PeakStartHour") == null ? 18 : null),
            Hour("PeakEndHour") ?? (Value("PeakEndHour") == null ? 23 : null)));
    }
}
