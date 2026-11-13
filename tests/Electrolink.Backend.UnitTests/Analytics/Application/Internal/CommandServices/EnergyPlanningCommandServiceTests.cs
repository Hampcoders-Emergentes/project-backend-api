using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Analytics.Domain.Repositories;
using static Electrolink.Backend.UnitTests.Analytics.Application.Internal.AnalyticsTestData;

namespace Electrolink.Backend.UnitTests.Analytics.Application.Internal.CommandServices;

public class EnergyPlanningCommandServiceTests
{
    private readonly Fake<IStoreMeasurementPort> _stores = Measurements();
    private readonly Fake<IEnergyPlanningRepository> _repository = new();

    private EnergyPlanningCommandService CreateSut() => new(_stores.Object, _repository.Object, Rates().Object);

    [Fact]
    public async Task Handle_GetOpeningHoursWhenStoreIsNotAccessible_ThrowsStoreNotAccessibleException()
    {
        // Arrange
        _stores.Returns(nameof(IStoreMeasurementPort.CanAccess), false);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new GetStoreOpeningHoursQuery(Ids.Actor, Ids.Store), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<StoreNotAccessibleException>(act);
    }

    [Fact]
    public async Task Handle_GetOpeningHoursWithStoredDays_ReturnsDaysOrderedFromMonday()
    {
        // Arrange
        _repository.Returns(nameof(IEnergyPlanningRepository.OpeningHours), new List<StoreOpeningSchedule>
        {
            StoreOpeningSchedule.Define(Ids.Store, DayOfWeek.Sunday, new TimeOnly(10, 0), new TimeOnly(14, 0)),
            StoreOpeningSchedule.Define(Ids.Store, DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(18, 30))
        });
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetStoreOpeningHoursQuery(Ids.Actor, Ids.Store), CancellationToken.None);

        // Assert
        Assert.Equal(["MONDAY", "SUNDAY"], result.Days.Select(d => d.DayOfWeek));
    }

    [Fact]
    public async Task Handle_GetOpeningHoursWithStoredDay_FormatsTimesAsHourMinute()
    {
        // Arrange
        _repository.Returns(nameof(IEnergyPlanningRepository.OpeningHours), new List<StoreOpeningSchedule>
        {
            StoreOpeningSchedule.Define(Ids.Store, DayOfWeek.Monday, new TimeOnly(8, 5), new TimeOnly(18, 30))
        });
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetStoreOpeningHoursQuery(Ids.Actor, Ids.Store), CancellationToken.None);

        // Assert
        Assert.Equal(new OpeningDayResource("MONDAY", "08:05", "18:30"), Assert.Single(result.Days));
    }

    [Fact]
    public async Task Handle_GetOpeningHoursWithoutStoredDays_ReturnsNotConfigured()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetStoreOpeningHoursQuery(Ids.Actor, Ids.Store), CancellationToken.None);

        // Assert
        Assert.False(result.IsConfigured);
    }

    [Fact]
    public async Task Handle_SetOpeningHoursWithDuplicateDay_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();
        var command = new SetStoreOpeningHoursCommand(Ids.Actor, Ids.Store,
            [new("MONDAY", "08:00", "18:00"), new("monday", "09:00", "17:00")]);

        // Act
        var act = () => sut.Handle(command, CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_SetOpeningHoursWithUnknownDay_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();
        var command = new SetStoreOpeningHoursCommand(Ids.Actor, Ids.Store, [new("FUNDAY", "08:00", "18:00")]);

        // Act
        var act = () => sut.Handle(command, CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_SetOpeningHoursWithMalformedTime_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();
        var command = new SetStoreOpeningHoursCommand(Ids.Actor, Ids.Store, [new("MONDAY", "8am", "18:00")]);

        // Act
        var act = () => sut.Handle(command, CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_SetOpeningHoursWithValidDays_ReplacesStoredSchedule()
    {
        // Arrange
        var sut = CreateSut();
        var command = new SetStoreOpeningHoursCommand(Ids.Actor, Ids.Store,
            [new("MONDAY", "08:00", "18:00"), new("TUESDAY", "08:00", "18:00")]);

        // Act
        await sut.Handle(command, CancellationToken.None);

        // Assert
        var replaced = Assert.Single(_repository.CallsTo(nameof(IEnergyPlanningRepository.ReplaceOpeningHours)));
        Assert.Equal(2, replaced.Arg<IReadOnlyList<StoreOpeningSchedule>>(1).Count);
    }

    [Fact]
    public async Task Handle_SetOpeningHoursWithLowercaseDay_ReturnsNormalizedDayName()
    {
        // Arrange
        var sut = CreateSut();
        var command = new SetStoreOpeningHoursCommand(Ids.Actor, Ids.Store, [new(" tuesday ", "22:00", "06:00")]);

        // Act
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal("TUESDAY", Assert.Single(result.Days).DayOfWeek);
    }

    [Fact]
    public async Task Handle_SetBudgetWhenMonthHasNoBudget_AddsNewBudget()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new SetStoreEnergyBudgetCommand(Ids.Actor, Ids.Store, 2025, 3, 1200, 80), CancellationToken.None);

        // Assert
        Assert.Equal(1, _repository.CallCount(nameof(IEnergyPlanningRepository.Add)));
        Assert.Equal((2025, 3, 1200d, 80d), (result.Year, result.Month, result.TargetKWh, result.AlertThresholdPercentage));
    }

    [Fact]
    public async Task Handle_SetBudgetWhenMonthHasBudget_UpdatesExistingBudget()
    {
        // Arrange
        var existing = StoreEnergyBudget.Define(Ids.Store, 2025, 3, 500, 50, Ids.OtherUser, DateTime.UtcNow.AddDays(-1));
        _repository.Returns(nameof(IEnergyPlanningRepository.Budget), existing);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new SetStoreEnergyBudgetCommand(Ids.Actor, Ids.Store, 2025, 3, 900, 75), CancellationToken.None);

        // Assert
        Assert.Equal(0, _repository.CallCount(nameof(IEnergyPlanningRepository.Add)));
        Assert.Equal(900, result.TargetKWh);
    }

    [Fact]
    public async Task Handle_SetBudgetWithInvalidThreshold_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new SetStoreEnergyBudgetCommand(Ids.Actor, Ids.Store, 2025, 3, 900, 0), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_SetBudgetWhenStoreIsNotAccessible_ThrowsStoreNotAccessibleException()
    {
        // Arrange
        _stores.Returns(nameof(IStoreMeasurementPort.CanAccess), false);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new SetStoreEnergyBudgetCommand(Ids.Actor, Ids.Store, 2025, 3, 900, 75), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<StoreNotAccessibleException>(act);
    }
}
