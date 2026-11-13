using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Analytics.Domain.Repositories;
using Hampcoders.Electrolink.API.Analytics.Domain.Services;
using static Electrolink.Backend.UnitTests.Analytics.Application.Internal.AnalyticsTestData;

namespace Electrolink.Backend.UnitTests.Analytics.Application.Internal.QueryServices;

public class EnergyInsightsQueryServiceTests
{
    private static readonly DateTime Period2Start = Start.AddDays(1);
    private readonly Fake<IStoreMeasurementPort> _measurements = Measurements();
    private readonly Fake<IEnergyPlanningRepository> _planning = new();

    private EnergyInsightsQueryService CreateSut() => new(_measurements.Object, Rates().Object, _planning.Object);

    private static CompareConsumptionPeriodsQuery CompareQuery() =>
        new(Ids.Actor, Ids.Store, Start, Start.AddHours(1), Period2Start, Period2Start.AddHours(1));

    private void GivenOpeningHours() => _planning.Returns(nameof(IEnergyPlanningRepository.OpeningHours), new List<StoreOpeningSchedule>
    {
        StoreOpeningSchedule.Define(Ids.Store, DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(18, 0))
    });

    [Fact]
    public async Task Handle_CompareWhenSecondPeriodDoublesLoad_ReportsHundredPercentIncrease()
    {
        // Arrange
        var samples = ConstantLoad("eq-1", Start, 1, 1).Concat(ConstantLoad("eq-1", Period2Start, 1, 2));
        _measurements.Returns(nameof(IStoreMeasurementPort.Load), Set(new Dictionary<string, string> { ["eq-1"] = "Fridge" }, samples));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(CompareQuery(), CancellationToken.None);

        // Assert
        Assert.Equal(100d, result.KwhChangePercent);
    }

    [Fact]
    public async Task Handle_CompareWithEnoughSamplesInBothPeriods_ReportsSufficientData()
    {
        // Arrange
        var samples = ConstantLoad("eq-1", Start, 1, 1).Concat(ConstantLoad("eq-1", Period2Start, 1, 2));
        _measurements.Returns(nameof(IStoreMeasurementPort.Load), Set(new Dictionary<string, string> { ["eq-1"] = "Fridge" }, samples));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(CompareQuery(), CancellationToken.None);

        // Assert
        Assert.Equal(DataQuality.Sufficient, result.DataQuality);
    }

    [Fact]
    public async Task Handle_CompareWithoutSamples_ReportsInsufficientDataAndNullChange()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(CompareQuery(), CancellationToken.None);

        // Assert
        Assert.Equal((DataQuality.InsufficientData, (double?)null), (result.DataQuality, result.KwhChangePercent));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1001)]
    [InlineData(double.NaN)]
    public async Task Handle_OffHoursWithThresholdOutOfRange_ThrowsArgumentException(double threshold)
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new GetOffHoursConsumptionQuery(Ids.Actor, Ids.Store, Start, Start.AddDays(1), threshold),
            CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_OffHoursWhenStoreIsNotAccessible_ThrowsStoreNotAccessibleException()
    {
        // Arrange
        _measurements.Returns(nameof(IStoreMeasurementPort.CanAccess), false);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new GetOffHoursConsumptionQuery(Ids.Actor, Ids.Store, Start, Start.AddDays(1), 0.1), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<StoreNotAccessibleException>(act);
    }

    [Fact]
    public async Task Handle_OffHoursWithoutOpeningHours_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new GetOffHoursConsumptionQuery(Ids.Actor, Ids.Store, Start, Start.AddDays(1), 0.1), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_OffHoursWithoutSamples_ReturnsNoEpisodes()
    {
        // Arrange
        GivenOpeningHours();
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetOffHoursConsumptionQuery(Ids.Actor, Ids.Store, Start, Start.AddDays(1), 0.1), CancellationToken.None);

        // Assert
        Assert.Equal((0d, 0), (result.TotalKwh, result.Episodes.Count));
    }

    [Fact]
    public async Task Handle_BudgetStatusWhenStoreIsNotAccessible_ThrowsStoreNotAccessibleException()
    {
        // Arrange
        _measurements.Returns(nameof(IStoreMeasurementPort.CanAccess), false);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new GetBudgetStatusQuery(Ids.Actor, Ids.Store, 2025, 1), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<StoreNotAccessibleException>(act);
    }

    [Theory]
    [InlineData(2025, 13)]
    [InlineData(1999, 1)]
    public async Task Handle_BudgetStatusWithInvalidPeriod_ThrowsArgumentException(int year, int month)
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new GetBudgetStatusQuery(Ids.Actor, Ids.Store, year, month), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_BudgetStatusWithoutBudget_ReturnsNoBudgetStatus()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetBudgetStatusQuery(Ids.Actor, Ids.Store, 2025, 1), CancellationToken.None);

        // Assert
        Assert.Equal(("NO_BUDGET", (double?)null), (result.Status, result.TargetKWh));
    }

    [Fact]
    public async Task Handle_BudgetStatusBelowThreshold_ReturnsOnTrackWithPercentages()
    {
        // Arrange
        _measurements.Returns(nameof(IStoreMeasurementPort.Load), Set(new Dictionary<string, string> { ["eq-1"] = "Fridge" },
            ConstantLoad("eq-1", Start, 1, 2)));
        _planning.Returns(nameof(IEnergyPlanningRepository.Budget),
            StoreEnergyBudget.Define(Ids.Store, 2025, 1, 100, 80, Ids.User, DateTime.UtcNow));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetBudgetStatusQuery(Ids.Actor, Ids.Store, 2025, 1), CancellationToken.None);

        // Assert
        Assert.Equal((BudgetStatus.OnTrack, 2d, 98d), (result.Status, result.ConsumedPercentage, result.RemainingKwh));
    }

    [Fact]
    public async Task Handle_BudgetStatusAboveTarget_ReturnsExceededWarning()
    {
        // Arrange
        _measurements.Returns(nameof(IStoreMeasurementPort.Load), Set(new Dictionary<string, string> { ["eq-1"] = "Fridge" },
            ConstantLoad("eq-1", Start, 1, 2)));
        _planning.Returns(nameof(IEnergyPlanningRepository.Budget),
            StoreEnergyBudget.Define(Ids.Store, 2025, 1, 1, 80, Ids.User, DateTime.UtcNow));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetBudgetStatusQuery(Ids.Actor, Ids.Store, 2025, 1), CancellationToken.None);

        // Assert
        Assert.Equal((BudgetStatus.Exceeded, true), (result.Status, result.IsWarning));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(32)]
    public async Task Handle_MaintenancePredictionsWithLookbackOutOfRange_ThrowsArgumentException(int days)
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new GetMaintenancePredictionsQuery(Ids.Actor, Ids.Store, days), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_MaintenancePredictionsForStoreWithoutEquipment_ReportsInsufficientData()
    {
        // Arrange
        _measurements.Returns(nameof(IStoreMeasurementPort.LoadElectrical), new StoreElectricalSet(new Dictionary<string, string>(),
            new Dictionary<string, EquipmentOperatingLimits>(), []));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetMaintenancePredictionsQuery(Ids.Actor, Ids.Store, 7), CancellationToken.None);

        // Assert
        Assert.Equal((DataQuality.InsufficientData, 0), (result.DataQuality, result.SampleCount));
    }

    [Fact]
    public async Task Handle_MaintenancePredictionsForEquipmentWithoutSamples_ListsEquipmentAsInsufficient()
    {
        // Arrange
        _measurements.Returns(nameof(IStoreMeasurementPort.LoadElectrical), new StoreElectricalSet(
            new Dictionary<string, string> { ["eq-1"] = "Fridge" },
            new Dictionary<string, EquipmentOperatingLimits> { ["eq-1"] = new(220, 10, 0.85) }, []));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetMaintenancePredictionsQuery(Ids.Actor, Ids.Store, 7), CancellationToken.None);

        // Assert
        Assert.Equal(["eq-1"], result.InsufficientDataEquipmentIds);
    }
}
