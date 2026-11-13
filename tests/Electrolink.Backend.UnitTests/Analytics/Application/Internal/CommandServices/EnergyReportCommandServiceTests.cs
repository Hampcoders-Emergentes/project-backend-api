using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.Aggregates;
using static Electrolink.Backend.UnitTests.Analytics.Application.Internal.AnalyticsTestData;

namespace Electrolink.Backend.UnitTests.Analytics.Application.Internal.CommandServices;

public class EnergyReportCommandServiceTests
{
    private readonly Fake<IStoreMeasurementPort> _measurements = Measurements();
    private readonly Fake<IEnergyReportRenderer> _renderer = new();
    private readonly Fake<IEnergyReportStorage> _storage = new();

    public EnergyReportCommandServiceTests() =>
        _renderer.On(nameof(IEnergyReportRenderer.Render), a => new ReportFile("report." + a[1], "text/plain", [1, 2, 3]));

    private EnergyReportCommandService CreateSut() => new(_measurements.Object, Rates().Object, _renderer.Object, _storage.Object);

    private static GenerateEnergyReportCommand Command(string format = "csv", DateTime? to = null) =>
        new(Ids.Actor, Ids.Store, Start, to ?? Start.AddHours(1), format);

    [Fact]
    public async Task Handle_GenerateWithUnsupportedFormat_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Command("xlsx"), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_GenerateWithStartInFuture_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();
        var command = new GenerateEnergyReportCommand(Ids.Actor, Ids.Store, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(2), "csv");

        // Act
        var act = () => sut.Handle(command, CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_GenerateWithValidRequest_StoresRenderedReport()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var metadata = await sut.Handle(Command(), CancellationToken.None);

        // Assert
        var stored = Assert.Single(_storage.CallsTo(nameof(IEnergyReportStorage.Store))).Arg<EnergyReport>();
        Assert.Equal(metadata.ReportId, stored.Id);
    }

    [Fact]
    public async Task Handle_GenerateWithUppercaseFormat_ReturnsNormalizedFormat()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var metadata = await sut.Handle(Command(" PDF "), CancellationToken.None);

        // Assert
        Assert.Equal("pdf", metadata.Format);
    }

    [Fact]
    public async Task Handle_GenerateWithEndInFuture_ClampsEndToNow()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var metadata = await sut.Handle(Command(to: DateTime.UtcNow.AddDays(3)), CancellationToken.None);

        // Assert
        Assert.True(metadata.ToUtc <= DateTime.UtcNow);
    }

    [Fact]
    public async Task Handle_GenerateWithConstantLoad_RendersIntegratedEnergyAndCost()
    {
        // Arrange
        _measurements.Returns(nameof(IStoreMeasurementPort.Load),
            Set(new Dictionary<string, string> { ["eq-1"] = "Fridge" }, ConstantLoad("eq-1", Start, 1, 2)));
        var sut = CreateSut();

        // Act
        await sut.Handle(Command(), CancellationToken.None);

        // Assert
        var data = Assert.Single(_renderer.CallsTo(nameof(IEnergyReportRenderer.Render))).Arg<EnergyReportData>();
        Assert.Equal((2d, 1.0m, 100d), (data.TotalKwh, data.TotalCost, data.Equipment.Single().SharePercent));
    }

    [Fact]
    public async Task Handle_GenerateWithoutSamples_RendersZeroSharePerEquipment()
    {
        // Arrange
        _measurements.Returns(nameof(IStoreMeasurementPort.Load), Set(new Dictionary<string, string> { ["eq-1"] = "Fridge" }, []));
        var sut = CreateSut();

        // Act
        await sut.Handle(Command(), CancellationToken.None);

        // Assert
        var data = Assert.Single(_renderer.CallsTo(nameof(IEnergyReportRenderer.Render))).Arg<EnergyReportData>();
        Assert.Equal(0, data.Equipment.Single().SharePercent);
    }

    [Fact]
    public async Task Handle_GetFileWhenStoreIsNotAccessible_ThrowsStoreNotAccessibleException()
    {
        // Arrange
        _measurements.Returns(nameof(IStoreMeasurementPort.CanAccess), false);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new GetEnergyReportFileQuery(Ids.Actor, Ids.Store, "report-1"), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<StoreNotAccessibleException>(act);
    }

    [Fact]
    public async Task Handle_GetFileWhenReportIsMissing_ThrowsEnergyReportNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new GetEnergyReportFileQuery(Ids.Actor, Ids.Store, "report-1"), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<EnergyReportNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_GetFileWhenReportExpired_ThrowsEnergyReportNotFoundException()
    {
        // Arrange
        var expired = EnergyReport.Create(Ids.Store, Ids.User, "csv", "r.csv", "text/csv", [1], Start, Start.AddHours(1),
            DateTime.UtcNow - EnergyReport.Lifetime - TimeSpan.FromMinutes(1));
        _storage.Returns(nameof(IEnergyReportStorage.Find), expired);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new GetEnergyReportFileQuery(Ids.Actor, Ids.Store, expired.Id), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<EnergyReportNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_GetFileWhenReportIsValid_ReturnsReport()
    {
        // Arrange
        var report = EnergyReport.Create(Ids.Store, Ids.User, "csv", "r.csv", "text/csv", [1], Start, Start.AddHours(1), DateTime.UtcNow);
        _storage.Returns(nameof(IEnergyReportStorage.Find), report);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetEnergyReportFileQuery(Ids.Actor, Ids.Store, report.Id), CancellationToken.None);

        // Assert
        Assert.Same(report, result);
    }

    [Fact]
    public void Metadata_FromReport_MapsAllFields()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var report = EnergyReport.Create(Ids.Store, Ids.User, "pdf", "r.pdf", "application/pdf", [1, 2], Start, Start.AddHours(1), now);

        // Act
        var metadata = EnergyReportCommandService.Metadata(report);

        // Assert
        Assert.Equal(new EnergyReportMetadata(report.Id, Ids.Store, "pdf", "r.pdf", "application/pdf", 2, Start, Start.AddHours(1), now,
            now + EnergyReport.Lifetime), metadata);
    }
}
