using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.AlertManagement.Interfaces.ACL;

namespace Electrolink.Backend.UnitTests.AlertManagement.Interfaces.ACL;

public class StoreAlertHistoryFacadeTests
{
    private static readonly DateTime From = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly Fake<IAlertRepository> _alerts = new();

    [Fact]
    public async Task History_WithStoredAlert_MapsAlertWithAreaAndEquipmentNames()
    {
        // Arrange
        var alert = Alert.ReportManually("equipment-1", Ids.Actor, "sparks", "Sparks near the oven", null, From.AddHours(2));
        _alerts.Returns(nameof(IAlertRepository.History), new List<StoreAlertRecord> { new(alert, "Kitchen", "Oven") });
        var sut = new StoreAlertHistoryFacade(_alerts.Object);

        // Act
        var result = await sut.History(Ids.Store, From, From.AddDays(1), false, CancellationToken.None);

        // Assert
        var entry = Assert.Single(result);
        Assert.Equal((alert.Id, "equipment-1", "Oven", "Kitchen", "NON_CRITICAL", alert.Status),
            (entry.AlertId, entry.EquipmentId, entry.EquipmentName, entry.AreaName, entry.Severity, entry.Status));
    }

    [Fact]
    public async Task History_WhenCriticalOnly_FiltersRepositoryByCriticalSeverity()
    {
        // Arrange
        var sut = new StoreAlertHistoryFacade(_alerts.Object);

        // Act
        await sut.History(Ids.Store, From, From.AddDays(1), true, CancellationToken.None);

        // Assert
        Assert.Equal("CRITICAL", _alerts.Calls.Single().Args[4]);
    }

    [Fact]
    public async Task History_WhenAllSeverities_PassesNoSeverityFilter()
    {
        // Arrange
        var sut = new StoreAlertHistoryFacade(_alerts.Object);

        // Act
        await sut.History(Ids.Store, From, From.AddDays(1), false, CancellationToken.None);

        // Assert
        Assert.Null(_alerts.Calls.Single().Args[4]);
    }

    [Fact]
    public async Task History_WithoutAlerts_ReturnsEmptyList()
    {
        // Arrange
        var sut = new StoreAlertHistoryFacade(_alerts.Object);

        // Act
        var result = await sut.History(Ids.Store, From, From.AddDays(1), false, CancellationToken.None);

        // Assert
        Assert.Empty(result);
    }
}
