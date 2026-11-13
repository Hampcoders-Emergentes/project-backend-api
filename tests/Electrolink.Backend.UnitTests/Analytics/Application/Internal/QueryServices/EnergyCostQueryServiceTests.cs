using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.Enums;
using static Electrolink.Backend.UnitTests.Analytics.Application.Internal.AnalyticsTestData;

namespace Electrolink.Backend.UnitTests.Analytics.Application.Internal.QueryServices;

public class EnergyCostQueryServiceTests
{
    private readonly Fake<IStoreMeasurementPort> _measurements = Measurements();

    private EnergyCostQueryService CreateSut() => new(_measurements.Object, Rates().Object);

    private static GetEquipmentPowerConsumptionQuery EquipmentQuery(Granularity granularity = Granularity.Hourly) =>
        new(Ids.Actor, Ids.Store, "area-1", "eq-1", Start, Start.AddHours(1), granularity);

    private void GivenEquipmentSamples(double kw, string? name = "Fridge") =>
        _measurements.Returns(nameof(IStoreMeasurementPort.LoadEquipment), Set(
            name == null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["eq-1"] = name },
            ConstantLoad("eq-1", Start, 1, kw)));

    [Fact]
    public async Task Handle_EquipmentConsumptionWithConstantLoad_ReturnsIntegratedEnergy()
    {
        // Arrange
        GivenEquipmentSamples(2);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(EquipmentQuery(), CancellationToken.None);

        // Assert
        Assert.Equal((2d, 2d, 2d), (result.Kwh, result.PeakKw, result.AverageKw));
    }

    [Fact]
    public async Task Handle_EquipmentConsumptionWithConstantLoad_PricesEnergyAtBaseRate()
    {
        // Arrange
        GivenEquipmentSamples(2);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(EquipmentQuery(), CancellationToken.None);

        // Assert
        Assert.Equal(1.0m, result.Cost.TotalCost);
    }

    [Fact]
    public async Task Handle_EquipmentConsumptionWithoutKnownName_FallsBackToEquipmentId()
    {
        // Arrange
        GivenEquipmentSamples(2, name: null);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(EquipmentQuery(), CancellationToken.None);

        // Assert
        Assert.Equal("eq-1", result.EquipmentName);
    }

    [Fact]
    public async Task Handle_EquipmentConsumptionWithDailyGranularity_ReturnsOneBucketPerLocalDay()
    {
        // Arrange
        GivenEquipmentSamples(2);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(EquipmentQuery(Granularity.Daily), CancellationToken.None);

        // Assert
        Assert.Equal(new DateTime(2025, 1, 10, 0, 0, 0, DateTimeKind.Utc), Assert.Single(result.Series).StartUtc);
    }

    [Fact]
    public async Task Handle_EquipmentConsumptionWithoutSamples_ReturnsZeroPeakAndCount()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(EquipmentQuery(), CancellationToken.None);

        // Assert
        Assert.Equal((0d, 0), (result.PeakKw, result.SampleCount));
    }

    [Fact]
    public async Task Handle_ForecastWithoutSamples_ReturnsZeroMonthToDateConsumption()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetStoreCostForecastQuery(Ids.Actor, Ids.Store), CancellationToken.None);

        // Assert
        Assert.Equal((0d, 0m), (result.MonthToDateKwh, result.MonthToDateCost));
    }

    [Fact]
    public async Task Handle_ForecastWithEquipmentWithoutSamples_ListsEquipmentWithZeroCost()
    {
        // Arrange
        _measurements.Returns(nameof(IStoreMeasurementPort.Load), Set(new Dictionary<string, string> { ["eq-1"] = "Fridge" }, []));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetStoreCostForecastQuery(Ids.Actor, Ids.Store), CancellationToken.None);

        // Assert
        Assert.Equal(("eq-1", 0m), (Assert.Single(result.Equipment).EquipmentId, result.Equipment[0].ProjectedCost));
    }

    [Fact]
    public async Task Handle_ForecastWithRecentSamples_ReportsPositiveMonthToDateConsumption()
    {
        // Arrange
        var now = DateTime.UtcNow;
        _measurements.Returns(nameof(IStoreMeasurementPort.Load), Set(new Dictionary<string, string> { ["eq-1"] = "Fridge" },
            ConstantLoad("eq-1", now.AddMinutes(-20), 1.0 / 3, 3)));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetStoreCostForecastQuery(Ids.Actor, Ids.Store), CancellationToken.None);

        // Assert
        Assert.True(result.MonthToDateKwh > 0);
    }

    [Fact]
    public async Task Handle_Forecast_UsesTariffCurrencyAndTimeZone()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetStoreCostForecastQuery(Ids.Actor, Ids.Store), CancellationToken.None);

        // Assert
        Assert.Equal(("PEN", "UTC"), (result.Currency, result.TimeZone));
    }
}
