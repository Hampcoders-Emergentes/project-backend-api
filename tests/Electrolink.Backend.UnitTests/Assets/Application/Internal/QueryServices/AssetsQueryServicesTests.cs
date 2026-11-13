using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Assets.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Assets.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Entities;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using MediatR;
using static Electrolink.Backend.UnitTests.Assets.AssetsTestData;

namespace Electrolink.Backend.UnitTests.Assets.Application.Internal.QueryServices;

public class ComponentQueryServiceTests
{
    private readonly Fake<IComponentRepository> _components = new();

    [Fact]
    public async Task Handle_GetByIdWhenComponentExists_ReturnsComponent()
    {
        // Arrange
        var component = Component.Create("Breaker", "16A", true);
        _components.Returns(nameof(IComponentRepository.FindByIdAsync), component);
        var sut = new ComponentQueryService(_components.Object);

        // Act
        var result = await sut.Handle(new GetComponentByIdQuery(component.Id));

        // Assert
        Assert.Same(component, result);
    }

    [Fact]
    public async Task Handle_GetAll_ReturnsEveryComponent()
    {
        // Arrange
        _components.Returns(nameof(IComponentRepository.ListAsync), new[] { Component.Create("A", "a", true), Component.Create("B", "b", true) });
        var sut = new ComponentQueryService(_components.Object);

        // Act
        var result = await sut.Handle(new GetAllComponentsQuery());

        // Assert
        Assert.Equal(2, result.Count());
    }

    [Fact]
    public async Task Handle_GetByIds_PassesRequestedIdsToRepository()
    {
        // Arrange
        var ids = new[] { ComponentId.NewComponentId(), ComponentId.NewComponentId() };
        var sut = new ComponentQueryService(_components.Object);

        // Act
        await sut.Handle(new GetComponentsByIdsQuery(ids));

        // Assert
        Assert.Equal(ids, _components.Calls.Single().Arg<IEnumerable<ComponentId>>());
    }
}

public class ComponentTypeQueryServiceTests
{
    private readonly Fake<IComponentTypeRepository> _types = new();

    [Fact]
    public async Task Handle_GetByIdWhenTypeMissing_ReturnsNull()
    {
        // Arrange
        var sut = new ComponentTypeQueryService(_types.Object);

        // Act
        var result = await sut.Handle(new GetComponentTypeByIdQuery(ComponentTypeId.NewComponentTypeId()));

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_GetAll_ReturnsRepositoryTypes()
    {
        // Arrange
        _types.Returns(nameof(IComponentTypeRepository.ListAsync), new[] { ComponentType.Create("Sensors", "IoT") });
        var sut = new ComponentTypeQueryService(_types.Object);

        // Act
        var result = await sut.Handle(new GetAllComponentTypesQuery());

        // Assert
        Assert.Single(result);
    }

    [Fact]
    public async Task Handle_GetNameById_ReturnsRepositoryName()
    {
        // Arrange
        _types.Returns(nameof(IComponentTypeRepository.FindComponentTypeNameByIdAsync), "Sensors");
        var sut = new ComponentTypeQueryService(_types.Object);

        // Act
        var result = await sut.Handle(new GetComponentTypeNameByIdQuery(ComponentTypeId.NewComponentTypeId()));

        // Assert
        Assert.Equal("Sensors", result);
    }
}

public class IoTDeviceQueryServiceTests
{
    private readonly Fake<IIoTDeviceRepository> _devices = new();
    private readonly Fake<IPropertyRepository> _properties = new();
    private readonly Fake<IProfilesContextFacade> _profiles = HomeownerProfiles();
    private readonly Property _property = NewProperty();

    private IoTDeviceQueryService CreateSut() => new(_devices.Object, _properties.Object, _profiles.Object);

    [Fact]
    public async Task Handle_GetOwnedDeviceOnOwnedActiveProperty_ReturnsDevice()
    {
        // Arrange
        var device = AssignedDevice(_property);
        _devices.Returns(nameof(IIoTDeviceRepository.FindByIdAsync), device);
        _properties.Returns(nameof(IPropertyRepository.FindByIdAndOwnerAsync), _property);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetOwnedIoTDeviceByIdQuery(Ids.Actor, device.Id.Value));

        // Assert
        Assert.Same(device, result);
    }

    [Fact]
    public async Task Handle_GetOwnedDeviceNotAssignedToProperty_ReturnsNull()
    {
        // Arrange
        var device = InStockDevice();
        _devices.Returns(nameof(IIoTDeviceRepository.FindByIdAsync), device);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetOwnedIoTDeviceByIdQuery(Ids.Actor, device.Id.Value));

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_GetOwnedDeviceForCompanyActor_UsesCompanyOwnership()
    {
        // Arrange
        var property = NewProperty(ClientIdentity.FromCompany(Company));
        var device = AssignedDevice(property);
        _devices.Returns(nameof(IIoTDeviceRepository.FindByIdAsync), device);
        _properties.Returns(nameof(IPropertyRepository.FindByIdAndOwnerAsync), property);
        _profiles.Returns(nameof(IProfilesContextFacade.GetProfileClaimsAsync), Claims("Company", Company))
            .Returns(nameof(IProfilesContextFacade.IsCompanyActiveAsync), true);
        var sut = CreateSut();

        // Act
        await sut.Handle(new GetOwnedIoTDeviceByIdQuery(Ids.Actor, device.Id.Value));

        // Assert
        Assert.Equal(ClientIdentity.FromCompany(Company),
            _properties.CallsTo(nameof(IPropertyRepository.FindByIdAndOwnerAsync)).Single().Arg<ClientIdentity>(1));
    }

    [Fact]
    public async Task Handle_GetOwnedDeviceOnArchivedProperty_ReturnsNull()
    {
        // Arrange
        var device = AssignedDevice(_property);
        var archived = NewProperty();
        _properties.Returns(nameof(IPropertyRepository.FindByIdAsync), archived);
        await new PropertyCommandService(_properties.Object, new Fake<IUnitOfWork>().Object, new Fake<IMediator>().Object,
            new Fake<Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.IFileStorageService>().Object,
            new Fake<IOrphanedFileRepository>().Object).Handle(new ArchivePropertyCommand(archived.Id, "sold"));
        _devices.Returns(nameof(IIoTDeviceRepository.FindByIdAsync), device);
        _properties.Returns(nameof(IPropertyRepository.FindByIdAndOwnerAsync), archived);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetOwnedIoTDeviceByIdQuery(Ids.Actor, device.Id.Value));

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_GetBySerialNumber_ReturnsRepositoryDevice()
    {
        // Arrange
        var device = InStockDevice();
        _devices.Returns(nameof(IIoTDeviceRepository.FindBySerialNumberAsync), device);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetIoTDeviceBySerialNumberQuery(device.SerialNumber.Value));

        // Assert
        Assert.Same(device, result);
    }

    [Fact]
    public async Task Handle_GetByEmptySerialNumber_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new GetIoTDeviceBySerialNumberQuery(""));

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_GetByOwnedPropertyNotOwned_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetIoTDevicesByOwnedPropertyQuery(HomeownerOwner, _property.Id.Value));

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_GetByOwnedProperty_ReturnsPropertyDevices()
    {
        // Arrange
        var device = AssignedDevice(_property);
        _properties.Returns(nameof(IPropertyRepository.FindByIdAndOwnerAsync), _property);
        _devices.Returns(nameof(IIoTDeviceRepository.FindByPropertyIdAsync), new[] { device });
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetIoTDevicesByOwnedPropertyQuery(HomeownerOwner, _property.Id.Value));

        // Assert
        Assert.Same(device, Assert.Single(result!));
    }

    [Fact]
    public async Task Handle_GetActiveDeviceCount_ReturnsRepositoryCount()
    {
        // Arrange
        _devices.Returns(nameof(IIoTDeviceRepository.CountActiveByOwnerIdAsync), 3);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetActiveDeviceCountByOwnerIdQuery(Homeowner));

        // Assert
        Assert.Equal(3, result);
    }

    [Fact]
    public async Task Handle_GetByStatus_PassesStatusToRepository()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(new GetIoTDevicesByStatusQuery(EDeviceStatus.Maintenance));

        // Assert
        Assert.Equal(EDeviceStatus.Maintenance, _devices.Calls.Single().Arg<EDeviceStatus>());
    }
}

public class PropertyOwnerResolverTests
{
    private static User ActiveUser() => User.Create(Email.From("owner@electrolink.pe"), HashedPassword.FromHash("h"), AccessRole.User);

    [Fact]
    public async Task ResolveAsync_ForSuspendedUser_ReturnsNull()
    {
        // Arrange
        var user = ActiveUser();
        user.Suspend("fraud");

        // Act
        var result = await PropertyOwnerResolver.ResolveAsync(HomeownerProfiles().Object, user);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_ForConcordantHomeowner_ReturnsHomeownerIdentity()
    {
        // Arrange
        var profiles = HomeownerProfiles().Returns(nameof(IProfilesContextFacade.GetProfileIdByHomeownerIdAsync), "prof-1");

        // Act
        var result = await PropertyOwnerResolver.ResolveAsync(profiles.Object, ActiveUser());

        // Assert
        Assert.Equal(ClientIdentity.FromHomeowner(Homeowner), result);
    }

    [Fact]
    public async Task ResolveAsync_ForConcordantCompany_ReturnsCompanyIdentity()
    {
        // Arrange
        var profiles = new Fake<IProfilesContextFacade>()
            .Returns(nameof(IProfilesContextFacade.GetProfileClaimsAsync), Claims("Company", Company))
            .Returns(nameof(IProfilesContextFacade.GetProfileIdByCompanyIdAsync), "prof-1");

        // Act
        var result = await PropertyOwnerResolver.ResolveAsync(profiles.Object, ActiveUser());

        // Assert
        Assert.Equal(ClientIdentity.FromCompany(Company), result);
    }

    [Fact]
    public async Task ResolveAsync_WhenHomeownerBelongsToOtherProfile_ReturnsNull()
    {
        // Arrange
        var profiles = HomeownerProfiles().Returns(nameof(IProfilesContextFacade.GetProfileIdByHomeownerIdAsync), "prof-other");

        // Act
        var result = await PropertyOwnerResolver.ResolveAsync(profiles.Object, ActiveUser());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_ForTechnicianProfile_ReturnsNull()
    {
        // Arrange
        var profiles = TechnicianProfiles();

        // Act
        var result = await PropertyOwnerResolver.ResolveAsync(profiles.Object, ActiveUser());

        // Assert
        Assert.Null(result);
    }
}

public class TechnicianActorResolverTests
{
    private static User ActiveUser() => User.Create(Email.From("tech@electrolink.pe"), HashedPassword.FromHash("h"), AccessRole.User);

    [Fact]
    public async Task ResolveAsync_ForLinkedActiveTechnician_ReturnsTechnicianId()
    {
        // Arrange
        var profiles = TechnicianProfiles();

        // Act
        var result = await TechnicianActorResolver.ResolveAsync(profiles.Object, ActiveUser());

        // Assert
        Assert.Equal(TechnicianId.From(Ids.Technician), result);
    }

    [Fact]
    public async Task ResolveAsync_WhenTechnicianLinkDiffers_ReturnsNull()
    {
        // Arrange
        var profiles = TechnicianProfiles().Returns(nameof(IProfilesContextFacade.GetTechnicianIdByUserIdAsync), "tech-other");

        // Act
        var result = await TechnicianActorResolver.ResolveAsync(profiles.Object, ActiveUser());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_ForSuspendedUser_ReturnsNull()
    {
        // Arrange
        var user = ActiveUser();
        user.Suspend("fraud");

        // Act
        var result = await TechnicianActorResolver.ResolveAsync(TechnicianProfiles().Object, user);

        // Assert
        Assert.Null(result);
    }
}

public class PropertyPortfolioQueryServiceTests
{
    [Fact]
    public async Task Handle_GetByOwner_ReturnsPortfolioWithEntries()
    {
        // Arrange
        var portfolio = PropertyPortfolio.Create(HomeownerOwner);
        var repository = new Fake<IPropertyPortfolioRepository>()
            .Returns(nameof(IPropertyPortfolioRepository.FindByOwnerWithEntriesAsync), portfolio);
        var sut = new PropertyPortfolioQueryService(repository.Object, new Fake<IUnitOfWork>().Object, new Fake<IMediator>().Object);

        // Act
        var result = await sut.Handle(new GetPortfolioByOwnerIdQuery(HomeownerOwner));

        // Assert
        Assert.Same(portfolio, result);
    }
}

public class PropertyQueryServiceTests
{
    private readonly Fake<IPropertyRepository> _properties = new();
    private readonly Property _property = NewProperty();

    [Fact]
    public async Task Handle_GetById_FiltersByOwner()
    {
        // Arrange
        var sut = new PropertyQueryService(_properties.Object);

        // Act
        await sut.Handle(new GetPropertyByIdQuery(_property.Id, HomeownerOwner));

        // Assert
        Assert.Equal(HomeownerOwner, _properties.CallsTo(nameof(IPropertyRepository.FindByIdAndOwnerAsync)).Single().Arg<ClientIdentity>(1));
    }

    [Fact]
    public async Task Handle_GetByHomeownerAndId_ReturnsOwnedProperty()
    {
        // Arrange
        _properties.Returns(nameof(IPropertyRepository.FindByIdAndOwnerAsync), _property);
        var sut = new PropertyQueryService(_properties.Object);

        // Act
        var result = await sut.Handle(new GetPropertyByHomeownerIdAndIdQuery(_property.Id, HomeownerOwner));

        // Assert
        Assert.Same(_property, result);
    }

    [Fact]
    public async Task Handle_GetAllByOwner_ForwardsFiltersAndPaging()
    {
        // Arrange
        var sut = new PropertyQueryService(_properties.Object);

        // Act
        await sut.Handle(new GetAllPropertiesByOwnerIdQuery(HomeownerOwner, "Lima", "Main", 2, 20));

        // Assert
        Assert.Equal(new object?[] { HomeownerOwner, "Lima", "Main", 2, 20 }, _properties.Calls.Single().Args);
    }

    [Fact]
    public async Task Handle_GetAddressWhenPropertyExists_ReturnsAddress()
    {
        // Arrange
        _properties.Returns(nameof(IPropertyRepository.FindByIdAsync), _property);
        var sut = new PropertyQueryService(_properties.Object);

        // Act
        var result = await sut.Handle(new GetPropertyAddressQuery(_property.Id));

        // Assert
        Assert.Equal(_property.Address, result);
    }

    [Fact]
    public async Task Handle_GetAddressWhenPropertyMissing_ReturnsNull()
    {
        // Arrange
        var sut = new PropertyQueryService(_properties.Object);

        // Act
        var result = await sut.Handle(new GetPropertyAddressQuery(_property.Id));

        // Assert
        Assert.Null(result);
    }
}

public class TechnicianInventoryQueryServiceTests
{
    private static readonly TechnicianId Technician = TechnicianId.From(Ids.Technician);
    private readonly Fake<ITechnicianInventoryRepository> _inventories = new();
    private readonly Fake<IComponentRepository> _components = new();
    private readonly Component _component = Component.Create("Breaker", "16A", true);
    private readonly ComponentType _type = ComponentType.Create("Protection", "Breakers");

    private TechnicianInventoryQueryService CreateSut() => new(_inventories.Object, _components.Object);

    private async Task<TechnicianInventory> GivenInventoryWithStock(int quantity)
    {
        var inventory = TechnicianInventory.Create(Technician);
        _inventories.Returns(nameof(ITechnicianInventoryRepository.FindByTechnicianIdAsync), inventory);
        var components = new Fake<IComponentRepository>().Returns(nameof(IComponentRepository.FindByIdAsync), _component);
        var types = new Fake<IComponentTypeRepository>().Returns(nameof(IComponentTypeRepository.FindByIdAsync), _type);
        await new TechnicianInventoryCommandService(_inventories.Object, components.Object, types.Object, new Fake<IUnitOfWork>().Object,
            new Fake<IMediator>().Object).Handle(new AddStockToInventoryCommand(Technician, _component.Id, _type.Id, quantity, 1));
        return inventory;
    }

    [Fact]
    public async Task Handle_GetInventoryWhenMissing_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetInventoryByTechnicianIdQuery(Technician));

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_GetInventoryWithStock_IncludesComponentNames()
    {
        // Arrange
        await GivenInventoryWithStock(4);
        _components.Returns(nameof(IComponentRepository.FindByIdsAsync), new[] { _component });
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetInventoryByTechnicianIdQuery(Technician));

        // Assert
        Assert.Equal("Breaker", result!.ComponentNames[_component.Id.Value]);
    }

    [Fact]
    public async Task Handle_GetEmptyInventory_ReturnsNoComponentNames()
    {
        // Arrange
        _inventories.Returns(nameof(ITechnicianInventoryRepository.FindByTechnicianIdAsync), TechnicianInventory.Create(Technician));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetInventoryByTechnicianIdQuery(Technician));

        // Assert
        Assert.Empty(result!.ComponentNames);
    }

    [Fact]
    public async Task Handle_GetStockItemsWithoutStock_ReturnsEmpty()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetStockItemsByTechnicianIdQuery(Technician));

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_GetStockItemsWithUnknownComponent_UsesFallbackName()
    {
        // Arrange
        var inventory = await GivenInventoryWithStock(4);
        _inventories.Returns(nameof(ITechnicianInventoryRepository.FindStockItemsByTechnicianIdAsync), inventory.StockItems.ToList());
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetStockItemsByTechnicianIdQuery(Technician));

        // Assert
        Assert.Equal(("Unknown Component", 4), (Assert.Single(result).ComponentName, result.Single().QuantityAvailable));
    }

    [Fact]
    public async Task Handle_GetStockByTypeWithKnownComponent_MapsComponentName()
    {
        // Arrange
        var inventory = await GivenInventoryWithStock(4);
        _inventories.Returns(nameof(ITechnicianInventoryRepository.FindStockItemsByComponentTypeIdAndTechnicianIdAsync),
            inventory.StockItems.ToList());
        _components.Returns(nameof(IComponentRepository.FindByIdsAsync), new[] { _component });
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetComponentStockByComponentTypeIdAndTechnicianIdQuery(_type.Id, Technician));

        // Assert
        Assert.Equal("Breaker", Assert.Single(result).ComponentName);
    }

    [Fact]
    public async Task Handle_CheckStockWhenInventoryMissing_ReturnsFalse()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new CheckComponentTypeStockQuery(Ids.Technician, [("ctype-1", 1)]));

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public async Task Handle_CheckStockAgainstRequiredQuantity_ReturnsWhetherStockSuffices(int required, bool expected)
    {
        // Arrange
        await GivenInventoryWithStock(4);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new CheckComponentTypeStockQuery(Ids.Technician, [(_type.Id.Value, required)]));

        // Assert
        Assert.Equal(expected, result);
    }
}
