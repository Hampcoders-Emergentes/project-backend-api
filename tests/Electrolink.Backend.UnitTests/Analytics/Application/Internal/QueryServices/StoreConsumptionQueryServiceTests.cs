using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.Enums;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Analytics.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using static Electrolink.Backend.UnitTests.Analytics.Application.Internal.AnalyticsTestData;

namespace Electrolink.Backend.UnitTests.Analytics.Application.Internal.QueryServices;

public class StoreConsumptionQueryServiceTests
{
    private readonly Fake<IStoreMeasurementPort> _measurements = Measurements();
    private readonly Fake<IConsumptionReportExporter> _exporter = new();

    private StoreConsumptionQueryService CreateSut() => new(_measurements.Object, _exporter.Object);

    private static GetStoreConsumptionQuery Query() => new(Ids.Actor, Ids.Store, Start, Start.AddHours(1), Granularity.Hourly);

    [Fact]
    public async Task Handle_WithConstantLoad_ReturnsStoreTotal()
    {
        // Arrange
        _measurements.Returns(nameof(IStoreMeasurementPort.Load), Set(new Dictionary<string, string> { ["eq-1"] = "Fridge" },
            ConstantLoad("eq-1", Start, 1, 3)));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(Query(), CancellationToken.None);

        // Assert
        Assert.Equal(3, result.TotalKwh, 6);
    }

    [Fact]
    public async Task Handle_EquipmentWithoutSamples_AppearsWithZeroConsumption()
    {
        // Arrange
        _measurements.Returns(nameof(IStoreMeasurementPort.Load), Set(new Dictionary<string, string> { ["eq-1"] = "Fridge" }, []));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(Query(), CancellationToken.None);

        // Assert
        var equipment = Assert.Single(result.Equipment);
        Assert.Equal(("eq-1", 0d), (equipment.EquipmentId, equipment.Kwh));
    }

    [Fact]
    public async Task Handle_WithSeveralEquipment_OrdersMeasuredEquipmentByName()
    {
        // Arrange
        var samples = ConstantLoad("eq-b", Start, 1, 1).Concat(ConstantLoad("eq-a", Start, 1, 1));
        _measurements.Returns(nameof(IStoreMeasurementPort.Load), Set(
            new Dictionary<string, string> { ["eq-b"] = "Oven", ["eq-a"] = "Zeta cooler" }, samples));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(Query(), CancellationToken.None);

        // Assert
        Assert.Equal(["Oven", "Zeta cooler"], result.Equipment.Select(e => e.EquipmentName));
    }

    [Fact]
    public async Task Handle_SamplesOfUnnamedEquipment_UseEquipmentIdAsName()
    {
        // Arrange
        _measurements.Returns(nameof(IStoreMeasurementPort.Load), Set(new Dictionary<string, string>(), ConstantLoad("eq-9", Start, 1, 1)));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(Query(), CancellationToken.None);

        // Assert
        Assert.Equal("eq-9", Assert.Single(result.Equipment).EquipmentName);
    }

    [Fact]
    public async Task Handle_ExportQuery_ReturnsExporterFileForRequestedFormat()
    {
        // Arrange
        var file = new ReportFile("c.csv", "text/csv", [1]);
        _exporter.Returns(nameof(IConsumptionReportExporter.Export), file);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new ExportStoreConsumptionReportQuery(Query(), ExportFormat.Csv), CancellationToken.None);

        // Assert
        Assert.Same(file, result);
        Assert.Equal(ExportFormat.Csv, _exporter.Calls.Single().Arg<ExportFormat>(1));
    }
}

public class TechnicianMetricsQueryServiceTests
{
    [Fact]
    public async Task GetPerformanceDashboardAsync_WhenMetricsExist_ReturnsCurrentPeriodMetrics()
    {
        // Arrange
        var metrics = TechnicianMetrics.InitializeForPeriod(TechnicianId.From(Ids.Technician), DateRange.CurrentMonth());
        var repository = new Fake<ITechnicianMetricsRepository>()
            .Returns(nameof(ITechnicianMetricsRepository.FindCurrentPeriodByTechnicianAsync), metrics);
        var sut = new TechnicianMetricsQueryService(repository.Object);

        // Act
        var result = await sut.GetPerformanceDashboardAsync(Ids.Technician);

        // Assert
        Assert.Same(metrics, result);
    }

    [Fact]
    public async Task GetPerformanceDashboardAsync_WhenNoMetrics_ReturnsNull()
    {
        // Arrange
        var sut = new TechnicianMetricsQueryService(new Fake<ITechnicianMetricsRepository>().Object);

        // Act
        var result = await sut.GetPerformanceDashboardAsync(Ids.Technician);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetPerformanceDashboardAsync_WithMalformedTechnicianId_ThrowsInvalidIdException()
    {
        // Arrange
        var sut = new TechnicianMetricsQueryService(new Fake<ITechnicianMetricsRepository>().Object);

        // Act
        var act = () => sut.GetPerformanceDashboardAsync("x");

        // Assert
        await Assert.ThrowsAsync<InvalidIdException>(act);
    }
}
