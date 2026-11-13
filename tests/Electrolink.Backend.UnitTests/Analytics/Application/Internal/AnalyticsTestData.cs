using Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;
using Electrolink.Backend.UnitTests.TestDoubles;

namespace Electrolink.Backend.UnitTests.Analytics.Application.Internal;

internal static class AnalyticsTestData
{
    public static readonly DateTime Start = new(2025, 1, 10, 10, 0, 0, DateTimeKind.Utc);

    public static UtilityRateSchedule Tariff(decimal rate = 0.5m) => UtilityRateSchedule.Create("PEN", rate, "UTC");

    /// <summary>Samples every 10 minutes at constant power, covering [from, from + hours].</summary>
    public static List<PowerSample> ConstantLoad(string equipmentId, DateTime from, double hours, double kw)
    {
        var samples = new List<PowerSample>();
        for (var t = from; t <= from.AddHours(hours); t = t.AddMinutes(10)) samples.Add(new PowerSample(equipmentId, t, kw));
        return samples;
    }

    public static StoreMeasurementSet Set(IReadOnlyDictionary<string, string> names, IEnumerable<PowerSample> samples) =>
        new(names, samples.ToList());

    public static Fake<IUtilityRateProvider> Rates(UtilityRateSchedule? tariff = null) =>
        new Fake<IUtilityRateProvider>().Returns(nameof(IUtilityRateProvider.ForStore), tariff ?? Tariff());

    public static Fake<IStoreMeasurementPort> Measurements(bool canAccess = true, StoreMeasurementSet? set = null)
    {
        var fake = new Fake<IStoreMeasurementPort>().Returns(nameof(IStoreMeasurementPort.CanAccess), canAccess);
        var data = set ?? Set(new Dictionary<string, string>(), []);
        fake.Returns(nameof(IStoreMeasurementPort.Load), data).Returns(nameof(IStoreMeasurementPort.LoadEquipment), data);
        return fake;
    }
}
