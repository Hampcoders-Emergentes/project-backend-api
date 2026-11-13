using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Assets.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using MediatR;

namespace Electrolink.Backend.UnitTests.Assets.Application.Internal.CommandServices;

public class TechnicianInventoryCommandServiceTests
{
    private static readonly TechnicianId Technician = TechnicianId.From(Ids.Technician);
    private readonly Fake<ITechnicianInventoryRepository> _inventories = new();
    private readonly Fake<IComponentRepository> _components = new();
    private readonly Fake<IComponentTypeRepository> _types = new();
    private readonly Fake<IMediator> _mediator = new();
    private readonly Component _component = Component.Create("Breaker", "16A", true);
    private readonly ComponentType _type = ComponentType.Create("Protection", "Breakers");

    private TechnicianInventoryCommandService CreateSut() =>
        new(_inventories.Object, _components.Object, _types.Object, new Fake<IUnitOfWork>().Object, _mediator.Object);

    private TechnicianInventory GivenInventory()
    {
        var inventory = TechnicianInventory.Create(Technician);
        inventory.ClearDomainEvents();
        _inventories.Returns(nameof(ITechnicianInventoryRepository.FindByTechnicianIdAsync), inventory);
        return inventory;
    }

    private async Task<TechnicianInventory> GivenInventoryWithStock(int quantity, int threshold = 2)
    {
        var inventory = GivenInventory();
        _components.Returns(nameof(IComponentRepository.FindByIdAsync), _component);
        _types.Returns(nameof(IComponentTypeRepository.FindByIdAsync), _type);
        await CreateSut().Handle(new AddStockToInventoryCommand(Technician, _component.Id, _type.Id, quantity, threshold));
        return inventory;
    }

    private static int Available(TechnicianInventory inventory) => inventory.StockItems.Single().QuantityAvailable;

    [Fact]
    public async Task Handle_CreateWhenInventoryExists_ThrowsDuplicateAssetException()
    {
        // Arrange
        GivenInventory();
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new CreateTechnicianInventoryCommand(Technician));

        // Assert
        await Assert.ThrowsAsync<DuplicateAssetException>(act);
    }

    [Fact]
    public async Task Handle_CreateForNewTechnician_ReturnsEmptyInventory()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new CreateTechnicianInventoryCommand(Technician));

        // Assert
        Assert.Equal((Technician, EInventoryStatus.Empty), (result!.TechnicianId, result.Status));
        Assert.Equal(1, _inventories.CallCount(nameof(ITechnicianInventoryRepository.AddAsync)));
    }

    [Fact]
    public async Task Handle_AddStockWhenComponentMissing_ThrowsAssetNotFoundException()
    {
        // Arrange
        GivenInventory();
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new AddStockToInventoryCommand(Technician, _component.Id, _type.Id, 5, 1));

        // Assert
        await Assert.ThrowsAsync<AssetNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_AddStockWhenComponentTypeMissing_ThrowsAssetNotFoundException()
    {
        // Arrange
        GivenInventory();
        _components.Returns(nameof(IComponentRepository.FindByIdAsync), _component);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new AddStockToInventoryCommand(Technician, _component.Id, _type.Id, 5, 1));

        // Assert
        await Assert.ThrowsAsync<AssetNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_AddStockWhenInventoryMissing_ThrowsAssetNotFoundException()
    {
        // Arrange
        _components.Returns(nameof(IComponentRepository.FindByIdAsync), _component);
        _types.Returns(nameof(IComponentTypeRepository.FindByIdAsync), _type);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new AddStockToInventoryCommand(Technician, _component.Id, _type.Id, 5, 1));

        // Assert
        await Assert.ThrowsAsync<AssetNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_AddStockToEmptyInventory_ActivatesInventoryWithStock()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var inventory = await GivenInventoryWithStock(5);

        // Assert
        Assert.Equal((EInventoryStatus.Active, 5), (inventory.Status, Available(inventory)));
    }

    [Fact]
    public async Task Handle_UpdateStockWithHigherQuantity_RaisesAvailableQuantity()
    {
        // Arrange
        var inventory = await GivenInventoryWithStock(5);
        var sut = CreateSut();

        // Act
        await sut.Handle(new UpdateComponentStockCommand(Technician, _component.Id, 9, 2));

        // Assert
        Assert.Equal(9, Available(inventory));
    }

    [Fact]
    public async Task Handle_UpdateStockForUnknownComponent_ThrowsKeyNotFoundException()
    {
        // Arrange
        GivenInventory();
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new UpdateComponentStockCommand(Technician, ComponentId.NewComponentId(), 9, 2));

        // Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_RemoveStockWithRemainingQuantity_ThrowsInvalidOperationException()
    {
        // Arrange
        await GivenInventoryWithStock(5);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new RemoveComponentStockCommand(Technician, _component.Id));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_RemoveStockWithZeroQuantity_RemovesStockItem()
    {
        // Arrange
        var inventory = await GivenInventoryWithStock(0);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new RemoveComponentStockCommand(Technician, _component.Id));

        // Assert
        Assert.True(result);
        Assert.Empty(inventory.StockItems);
    }

    [Fact]
    public async Task Handle_ReserveWithEnoughStock_CreatesReservation()
    {
        // Arrange
        var inventory = await GivenInventoryWithStock(5);
        var sut = CreateSut();

        // Act
        await sut.Handle(new ReserveComponentsForServiceCommand(Technician, AssignmentId.NewAssignmentId(), [new(_component.Id, 3)]));

        // Assert
        Assert.Single(inventory.Reservations);
        Assert.Equal(3, inventory.StockItems.Single().ReservedQuantity);
    }

    [Fact]
    public async Task Handle_ReserveMoreThanAvailable_ThrowsInsufficientStockException()
    {
        // Arrange
        await GivenInventoryWithStock(2);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new ReserveComponentsForServiceCommand(Technician, AssignmentId.NewAssignmentId(), [new(_component.Id, 3)]));

        // Assert
        await Assert.ThrowsAsync<InsufficientStockException>(act);
    }

    [Fact]
    public async Task Handle_ConsumeReservedComponents_DeductsQuantity()
    {
        // Arrange
        var inventory = await GivenInventoryWithStock(5);
        var assignment = AssignmentId.NewAssignmentId();
        var sut = CreateSut();
        await sut.Handle(new ReserveComponentsForServiceCommand(Technician, assignment, [new(_component.Id, 3)]));

        // Act
        await sut.Handle(new ConsumeComponentsForServiceCommand(Technician, assignment));

        // Assert
        Assert.Equal(2, Available(inventory));
    }

    [Fact]
    public async Task Handle_ConsumeWithoutReservation_ThrowsReservationNotFoundException()
    {
        // Arrange
        await GivenInventoryWithStock(5);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new ConsumeComponentsForServiceCommand(Technician, AssignmentId.NewAssignmentId()));

        // Assert
        await Assert.ThrowsAsync<ReservationNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_ReleaseReservation_FreesReservedQuantity()
    {
        // Arrange
        var inventory = await GivenInventoryWithStock(5);
        var assignment = AssignmentId.NewAssignmentId();
        var sut = CreateSut();
        await sut.Handle(new ReserveComponentsForServiceCommand(Technician, assignment, [new(_component.Id, 3)]));

        // Act
        var result = await sut.Handle(new ReleaseReservationCommand(Technician, assignment, "cancelled"));

        // Assert
        Assert.True(result);
        Assert.Equal(0, inventory.StockItems.Single().ReservedQuantity);
    }

    [Fact]
    public async Task Handle_IncreaseStock_AddsToAvailableQuantity()
    {
        // Arrange
        var inventory = await GivenInventoryWithStock(5);
        var sut = CreateSut();

        // Act
        await sut.Handle(new IncreaseStockCommand(Technician, _component.Id, 4));

        // Assert
        Assert.Equal(9, Available(inventory));
    }

    [Fact]
    public async Task Handle_IncreaseStockWhenInventoryMissing_ThrowsAssetNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new IncreaseStockCommand(Technician, _component.Id, 4));

        // Assert
        await Assert.ThrowsAsync<AssetNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_DecreaseStockBelowThreshold_PublishesDecreaseAndLowStockEvents()
    {
        // Arrange
        await GivenInventoryWithStock(5, threshold: 2);
        var sut = CreateSut();
        var publishedBefore = _mediator.CallCount(nameof(IMediator.Publish));

        // Act
        await sut.Handle(new DecreaseStockCommand(Technician, _component.Id, 4));

        // Assert
        Assert.Equal(2, _mediator.CallCount(nameof(IMediator.Publish)) - publishedBefore);
    }

    [Fact]
    public async Task Handle_DecreaseStockWhenInventoryMissing_ThrowsAssetNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new DecreaseStockCommand(Technician, _component.Id, 1));

        // Assert
        await Assert.ThrowsAsync<AssetNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_AdjustWhenInventoryMissing_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new AdjustTechnicianInventoryCommand(Technician, [new(_component.Id, 1)]));

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_AdjustDownToZero_RemovesStockItem()
    {
        // Arrange
        var inventory = await GivenInventoryWithStock(3);
        var sut = CreateSut();

        // Act
        await sut.Handle(new AdjustTechnicianInventoryCommand(Technician, [new(_component.Id, -3)]));

        // Assert
        Assert.Empty(inventory.StockItems);
    }
}
