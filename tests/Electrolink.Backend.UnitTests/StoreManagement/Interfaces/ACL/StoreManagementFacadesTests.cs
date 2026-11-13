using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;
using Hampcoders.Electrolink.API.StoreManagement.Interfaces.ACL;

namespace Electrolink.Backend.UnitTests.StoreManagement.Interfaces.ACL;

internal static class StoreFixture
{
    public static Store Store() => Hampcoders.Electrolink.API.StoreManagement.Domain.Model.Store.Register(Ids.Actor, "Main shop", "Av. Lima 1");
}

public class StoreAccessFacadeTests
{
    private readonly Fake<IStoreHierarchyRepository> _hierarchy = new();

    private StoreAccessFacade CreateSut() => new(new StoreHierarchyService(_hierarchy.Object));

    [Fact]
    public async Task CanManage_WhenActorOwnsStore_ReturnsTrue()
    {
        // Arrange
        var store = StoreFixture.Store();
        _hierarchy.Returns(nameof(IStoreHierarchyRepository.FindStoreAsync), store);
        var sut = CreateSut();

        // Act
        var result = await sut.CanManage(Ids.Actor, store.Id, CancellationToken.None);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CanManage_WhenStoreNotFoundForActor_ReturnsFalse()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.CanManage(Ids.Actor, "store-" + Guid.NewGuid().ToString("N"), CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CanManage_WithMalformedStoreId_ReturnsFalseWithoutQuerying()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.CanManage(Ids.Actor, "shop-1", CancellationToken.None);

        // Assert
        Assert.False(result);
        Assert.Empty(_hierarchy.Calls);
    }
}

public class StoreMeasurementFacadeTests
{
    private readonly Fake<IStoreHierarchyRepository> _hierarchy = new();
    private readonly Fake<IStoreDashboardReadRepository> _dashboard = new();
    private readonly Fake<IEquipmentTelemetryRepository> _telemetry = new();
    private readonly Store _store = StoreFixture.Store();
    private static readonly DateTime From = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public StoreMeasurementFacadeTests() => _hierarchy.Returns(nameof(IStoreHierarchyRepository.FindStoreAsync), _store);

    private StoreMeasurementFacade CreateSut() => new(new StoreHierarchyService(_hierarchy.Object), _dashboard.Object, _telemetry.Object);

    [Fact]
    public async Task PowerSamples_WithTelemetry_ConvertsVoltageCurrentAndPowerFactorToKilowatts()
    {
        // Arrange
        _dashboard.Returns(nameof(IStoreDashboardReadRepository.EquipmentStatus),
            new List<EquipmentStatusRow> { new("equipment-1", "Fridge", "area-1", "Kitchen", true, 10, null, null) });
        _telemetry.Returns(nameof(IEquipmentTelemetryRepository.PowerSamples),
            new List<PowerSampleRow> { new("equipment-1", From, 220, 10, 0.5f) });
        var sut = CreateSut();

        // Act
        var result = await sut.PowerSamples(Ids.Actor, _store.Id, From, From.AddHours(1), CancellationToken.None);

        // Assert
        Assert.Equal("Fridge", result.EquipmentNames["equipment-1"]);
        Assert.Equal(1.1, Assert.Single(result.Samples).ActivePowerKw, 6);
    }

    [Fact]
    public async Task PowerSamples_WhenStoreNotOwned_ThrowsHierarchyNotFoundException()
    {
        // Arrange
        _hierarchy.Returns(nameof(IStoreHierarchyRepository.FindStoreAsync), null);
        var sut = CreateSut();

        // Act
        var act = () => sut.PowerSamples(Ids.Actor, _store.Id, From, From.AddHours(1), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<HierarchyNotFoundException>(act);
    }

    [Fact]
    public async Task EquipmentPowerSamples_ForOwnedEquipment_ReturnsOnlyThatEquipmentName()
    {
        // Arrange
        var area = StoreArea.Register(_store.Id, "Kitchen");
        var equipment = ElectricalEquipment.Register(area.Id, "Oven");
        _hierarchy.Returns(nameof(IStoreHierarchyRepository.FindAreaAsync), area)
            .Returns(nameof(IStoreHierarchyRepository.FindEquipmentAsync), equipment);
        var sut = CreateSut();

        // Act
        var result = await sut.EquipmentPowerSamples(Ids.Actor, _store.Id, area.Id, equipment.Id, From, From.AddHours(1), CancellationToken.None);

        // Assert
        Assert.Equal(new KeyValuePair<string, string>(equipment.Id, "Oven"), Assert.Single(result.EquipmentNames));
        Assert.Equal(equipment.Id, _telemetry.Calls.Single().Args[5]);
    }

    [Fact]
    public async Task EquipmentPowerSamples_WhenEquipmentMissing_ThrowsHierarchyNotFoundException()
    {
        // Arrange
        var area = StoreArea.Register(_store.Id, "Kitchen");
        _hierarchy.Returns(nameof(IStoreHierarchyRepository.FindAreaAsync), area);
        var sut = CreateSut();

        // Act
        var act = () => sut.EquipmentPowerSamples(Ids.Actor, _store.Id, area.Id, "equipment-" + Guid.NewGuid().ToString("N"), From,
            From.AddHours(1), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<HierarchyNotFoundException>(act);
    }

    [Fact]
    public async Task ElectricalSamples_WithLiveEquipment_MapsLimitsAndRawSamples()
    {
        // Arrange
        _dashboard.Returns(nameof(IStoreDashboardReadRepository.LiveEquipment), new List<EquipmentLiveRow>
        {
            new("equipment-1", "Fridge", "area-1", "Kitchen", ThresholdConfig.Default(), null, null, null)
        });
        _telemetry.Returns(nameof(IEquipmentTelemetryRepository.PowerSamples),
            new List<PowerSampleRow> { new("equipment-1", From, 221, 4, 0.9f) });
        var sut = CreateSut();

        // Act
        var result = await sut.ElectricalSamples(Ids.Actor, _store.Id, From, From.AddHours(1), CancellationToken.None);

        // Assert
        Assert.Equal(new EquipmentElectricalLimits("equipment-1", "Fridge", 220, 20, 0.85), Assert.Single(result.Equipment), new LimitsComparer());
        Assert.Equal((221d, 4d), (result.Samples.Single().Voltage, result.Samples.Single().Current));
    }

    private sealed class LimitsComparer : IEqualityComparer<EquipmentElectricalLimits>
    {
        public bool Equals(EquipmentElectricalLimits? x, EquipmentElectricalLimits? y) => x is not null && y is not null &&
            (x.EquipmentId, x.Name, x.NominalVoltage, x.MaxCurrentAmps) == (y.EquipmentId, y.Name, y.NominalVoltage, y.MaxCurrentAmps) &&
            Math.Abs(x.MinPowerFactor - y.MinPowerFactor) < 1e-6;

        public int GetHashCode(EquipmentElectricalLimits obj) => obj.EquipmentId.GetHashCode();
    }
}

public class StaffRosterFacadeTests
{
    private static readonly string ValidStore = "store-" + Guid.NewGuid().ToString("N");
    private readonly Fake<IKitchenOperationsRepository> _operations = new();

    private StaffRosterFacade CreateSut() =>
        new(new ShiftOperationsService(new StoreHierarchyService(new Fake<IStoreHierarchyRepository>().Object), _operations.Object, null!, null!));

    [Fact]
    public async Task OnDutyAt_WithoutShifts_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.OnDutyAt(Ids.Store, DateTime.UtcNow, CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task OnDutyAt_WithCoveringShift_ReturnsWorkerOnDuty()
    {
        // Arrange
        var now = DateTime.UtcNow;
        _operations.Returns(nameof(IKitchenOperationsRepository.ShiftsOverlapping), new List<StaffShiftAssignment>
        {
            StaffShiftAssignment.Schedule(ValidStore, "W-1", "Ana", "operational_staff", now.AddHours(-1), now.AddHours(3), Ids.User, now)
        });
        var sut = CreateSut();

        // Act
        var result = await sut.OnDutyAt(Ids.Store, now, CancellationToken.None);

        // Assert
        Assert.Equal(new OnDutyWorker("W-1", "Ana", StaffRole.OperationalStaff), result);
    }

    [Fact]
    public async Task OnDutyAt_WithManagerAndStaffOnShift_PrefersOperationalStaff()
    {
        // Arrange
        var now = DateTime.UtcNow;
        _operations.Returns(nameof(IKitchenOperationsRepository.ShiftsOverlapping), new List<StaffShiftAssignment>
        {
            StaffShiftAssignment.Schedule(ValidStore, "M-1", "Boss", StaffRole.StoreManager, now.AddHours(-2), now.AddHours(2), Ids.User, now),
            StaffShiftAssignment.Schedule(ValidStore, "W-2", "Luis", StaffRole.OperationalStaff, now.AddHours(-1), now.AddHours(1), Ids.User, now)
        });
        var sut = CreateSut();

        // Act
        var result = await sut.OnDutyAt(Ids.Store, now, CancellationToken.None);

        // Assert
        Assert.Equal("W-2", result!.WorkerCode);
    }
}

public class StoreNotificationRoutesFacadeTests
{
    private StoreNotificationRoutesFacade CreateSut() => new(new StoreNotificationPreferenceService(
        new StoreHierarchyService(new Fake<IStoreHierarchyRepository>().Object), new Fake<IStoreNotificationPreferenceRepository>().Object));

    [Theory]
    [InlineData(AlertUrgency.Critical)]
    [InlineData(AlertUrgency.NonCritical)]
    public async Task RoutesFor_WithoutConfiguredRoutes_ReturnsDefaultDashboardPush(string severity)
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.RoutesFor(Ids.Store, severity, CancellationToken.None);

        // Assert
        Assert.Equal(new StoreNotificationRoute(NotificationChannel.DashboardPush, null), Assert.Single(result));
    }

    [Fact]
    public async Task RoutesFor_WithUnknownSeverity_ReturnsNoRoutes()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.RoutesFor(Ids.Store, "LOW", CancellationToken.None);

        // Assert
        Assert.Empty(result);
    }
}
