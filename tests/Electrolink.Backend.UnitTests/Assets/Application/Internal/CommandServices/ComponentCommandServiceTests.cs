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
using Microsoft.Extensions.Logging.Abstractions;

namespace Electrolink.Backend.UnitTests.Assets.Application.Internal.CommandServices;

public class ComponentCommandServiceTests
{
    private readonly Fake<IComponentRepository> _components = new();
    private readonly Fake<IUnitOfWork> _unitOfWork = new();
    private readonly Fake<IMediator> _mediator = new();

    private ComponentCommandService CreateSut() => new(_components.Object, new Fake<IComponentTypeRepository>().Object, _unitOfWork.Object,
        _mediator.Object, NullLogger<ComponentCommandService>.Instance);

    [Fact]
    public async Task Handle_CreateWithDuplicateName_ThrowsDuplicateAssetException()
    {
        // Arrange
        _components.Returns(nameof(IComponentRepository.ExistsByNameAsync), true);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new CreateComponentCommand("Breaker", "16A", true));

        // Assert
        await Assert.ThrowsAsync<DuplicateAssetException>(act);
    }

    [Fact]
    public async Task Handle_CreateWithNewName_PersistsComponent()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new CreateComponentCommand("Breaker", "16A", true));

        // Assert
        Assert.Same(result, _components.CallsTo(nameof(IComponentRepository.AddAsync)).Single().Arg<Component>());
    }

    [Fact]
    public async Task Handle_CreateWithNewName_PublishesCreatedEventAndClearsEvents()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new CreateComponentCommand("Breaker", "16A", true));

        // Assert
        Assert.Equal(1, _mediator.CallCount(nameof(IMediator.Publish)));
        Assert.Empty(result!.DomainEvents);
    }

    [Fact]
    public async Task Handle_CreateWithEmptyDescription_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new CreateComponentCommand("Breaker", " ", true));

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_UpdateWhenComponentMissing_ThrowsAssetNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new UpdateComponentCommand(ComponentId.NewComponentId(), "New", null, true));

        // Assert
        await Assert.ThrowsAsync<AssetNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_UpdateWhenComponentExists_AppliesNewInfo()
    {
        // Arrange
        var component = Component.Create("Old", "desc", true);
        _components.Returns(nameof(IComponentRepository.FindByIdAsync), component);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new UpdateComponentCommand(component.Id, "New", null, false));

        // Assert
        Assert.Equal(("New", string.Empty, false), (result!.Name, result.Description, result.IsActive));
    }

    [Fact]
    public async Task Handle_DeleteWhenComponentMissing_ReturnsFalse()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new DeleteComponentCommand(ComponentId.NewComponentId()));

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task Handle_DeleteWhenComponentExists_DeactivatesComponent()
    {
        // Arrange
        var component = Component.Create("Breaker", "desc", true);
        _components.Returns(nameof(IComponentRepository.FindByIdAsync), component);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new DeleteComponentCommand(component.Id));

        // Assert
        Assert.True(result);
        Assert.False(component.IsActive);
    }
}

public class ComponentTypeCommandServiceTests
{
    private readonly Fake<IComponentTypeRepository> _types = new();
    private readonly Fake<ITechnicianInventoryRepository> _inventories = new();
    private readonly Fake<IUnitOfWork> _unitOfWork = new();
    private readonly Fake<IMediator> _mediator = new();

    private ComponentTypeCommandService CreateSut() => new(_types.Object, _inventories.Object, _unitOfWork.Object, _mediator.Object,
        NullLogger<ComponentTypeCommandService>.Instance);

    private ComponentType GivenExistingType()
    {
        var type = ComponentType.Create("Sensors", "IoT sensors");
        type.ClearDomainEvents();
        _types.Returns(nameof(IComponentTypeRepository.FindByIdAsync), type);
        return type;
    }

    [Fact]
    public async Task Handle_CreateWithDuplicateName_ThrowsDuplicateAssetException()
    {
        // Arrange
        _types.Returns(nameof(IComponentTypeRepository.ExistsByNameAsync), true);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new CreateComponentTypeCommand("Sensors", "IoT"));

        // Assert
        await Assert.ThrowsAsync<DuplicateAssetException>(act);
    }

    [Fact]
    public async Task Handle_CreateWithNewName_ReturnsActiveType()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new CreateComponentTypeCommand("Sensors", "IoT"));

        // Assert
        Assert.True(result!.IsActive);
        Assert.Equal(1, _types.CallCount(nameof(IComponentTypeRepository.AddAsync)));
    }

    [Fact]
    public async Task Handle_UpdateWhenTypeMissing_ThrowsAssetNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new UpdateComponentTypeCommand(ComponentTypeId.NewComponentTypeId(), "New", "d"));

        // Assert
        await Assert.ThrowsAsync<AssetNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_UpdateWhenTypeExists_RenamesType()
    {
        // Arrange
        var type = GivenExistingType();
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new UpdateComponentTypeCommand(type.Id, "Relays", null));

        // Assert
        Assert.Equal(("Relays", string.Empty), (result!.Name, result.Description));
    }

    [Fact]
    public async Task Handle_DeleteWhenTypeMissing_ReturnsFalse()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new DeleteComponentTypeCommand(ComponentTypeId.NewComponentTypeId()));

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task Handle_DeleteWhenTypeHasStock_ThrowsInvalidOperationException()
    {
        // Arrange
        var type = GivenExistingType();
        _inventories.Returns(nameof(ITechnicianInventoryRepository.ExistsStockItemsByComponentTypeId), true);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new DeleteComponentTypeCommand(type.Id));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_DeleteWhenTypeUnused_RemovesType()
    {
        // Arrange
        var type = GivenExistingType();
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new DeleteComponentTypeCommand(type.Id));

        // Assert
        Assert.True(result);
        Assert.Same(type, _types.CallsTo(nameof(IComponentTypeRepository.Remove)).Single().Arg<ComponentType>());
    }

    [Fact]
    public async Task Handle_DeactivateWhenTypeExists_MarksTypeInactive()
    {
        // Arrange
        var type = GivenExistingType();
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new DeactivateComponentTypeCommand(type.Id));

        // Assert
        Assert.False(result.IsActive);
    }

    [Fact]
    public async Task Handle_ActivateWhenTypeInactive_MarksTypeActive()
    {
        // Arrange
        var type = GivenExistingType();
        type.Deactivate();
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new ActivateComponentTypeCommand(type.Id));

        // Assert
        Assert.True(result.IsActive);
    }

    [Fact]
    public async Task Handle_ActivateWhenTypeMissing_ThrowsAssetNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new ActivateComponentTypeCommand(ComponentTypeId.NewComponentTypeId()));

        // Assert
        await Assert.ThrowsAsync<AssetNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_DeactivateWhenTypeMissing_ThrowsAssetNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new DeactivateComponentTypeCommand(ComponentTypeId.NewComponentTypeId()));

        // Assert
        await Assert.ThrowsAsync<AssetNotFoundException>(act);
    }
}
