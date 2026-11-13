using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Assets.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Assets.Application.Internal.QueryServices.ReadModels;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Assets.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using static Electrolink.Backend.UnitTests.Assets.AssetsTestData;

namespace Electrolink.Backend.UnitTests.Assets.Interfaces.REST.Transform;

public class AssetsResourceAssemblersTests
{
    [Fact]
    public void ComponentResource_ToResourceFromEntity_MapsAllFields()
    {
        // Arrange
        var component = Component.Create("Breaker", "16A", false);

        // Act
        var resource = ComponentResourceFromEntityAssembler.ToResourceFromEntity(component);

        // Assert
        Assert.Equal((component.Id.Value, "Breaker", "16A", false), (resource.ComponentId, resource.Name, resource.Description, resource.IsActive));
    }

    [Fact]
    public void ComponentTypeResource_ToResourceFromEntity_MapsAllFields()
    {
        // Arrange
        var type = ComponentType.Create("Sensors", "IoT");

        // Act
        var resource = ComponentTypeResourceFromEntityAssembler.ToResourceFromEntity(type);

        // Assert
        Assert.Equal((type.Id.Value, "Sensors", "IoT"), (resource.ComponentTypeId, resource.Name, resource.Description));
    }

    [Fact]
    public void IoTDeviceResource_ToResourceFromEntityInStock_HasNoAssignedProperty()
    {
        // Arrange
        var device = InStockDevice();

        // Act
        var resource = IoTDeviceResourceFromEntityAssembler.ToResourceFromEntity(device);

        // Assert
        Assert.Equal((device.Id.Value, EDeviceStatus.InStock, (string?)null), (resource.Id, resource.Status, resource.AssignedPropertyId));
    }

    [Fact]
    public void IoTDeviceResource_ToResourceFromEntityInstalled_MapsPropertyAndInstallDate()
    {
        // Arrange
        var property = NewProperty();
        var device = InstalledDevice(property);

        // Act
        var resource = IoTDeviceResourceFromEntityAssembler.ToResourceFromEntity(device);

        // Assert
        Assert.Equal((property.Id.Value, device.InstalledAt), (resource.AssignedPropertyId, resource.InstalledAt));
    }

    [Fact]
    public void PropertyPhotoResource_ToResource_MapsPhotoAndThumbnail()
    {
        // Arrange
        var photo = PropertyPhoto.Create("https://img/1.jpg", "prov-1");

        // Act
        var resource = PropertyPhotoResourceFromEntityAssembler.ToResource(photo, "https://img/1-thumb.jpg");

        // Assert
        Assert.Equal(("https://img/1.jpg", "prov-1", "https://img/1-thumb.jpg", photo.UploadedAt),
            (resource.PublicUrl, resource.ProviderId, resource.ThumbnailUrl, resource.UploadedAt));
    }

    [Fact]
    public void PropertyPhotoResource_ToResourceWithoutThumbnail_LeavesThumbnailNull()
    {
        // Act
        var resource = PropertyPhotoResourceFromEntityAssembler.ToResource(PropertyPhoto.Create("https://img/1.jpg", "prov-1"));

        // Assert
        Assert.Null(resource.ThumbnailUrl);
    }

    [Fact]
    public async Task PropertyPortfolioResource_ToResourceFromEntity_MapsEntries()
    {
        // Arrange
        var property = NewProperty();
        var portfolio = PropertyPortfolio.Create(HomeownerOwner);
        var portfolios = new Fake<IPropertyPortfolioRepository>().Returns(nameof(IPropertyPortfolioRepository.FindByOwnerWithEntriesAsync), portfolio);
        var properties = new Fake<IPropertyRepository>().Returns(nameof(IPropertyRepository.FindByIdAsync), property);
        await new PropertyPortfolioCommandService(portfolios.Object, properties.Object, new Fake<IUnitOfWork>().Object, new Fake<IMediator>().Object,
            NullLogger<PropertyPortfolioCommandService>.Instance).Handle(new AddPropertyToPortfolioCommand(HomeownerOwner, property.Id, "Home", true, "Vacant"));

        // Act
        var resource = PropertyPortfolioResourceFromEntityAssembler.ToResourceFromEntity(portfolio);

        // Assert
        var entry = Assert.Single(resource.Entries);
        Assert.Equal((property.Id.Value, "Home", true, "Vacant"), (entry.PropertyId, entry.Nickname, entry.IsPrimary, entry.OccupancyStatus));
    }

    [Fact]
    public void PropertyPortfolioResource_ToResourceFromEmptyPortfolio_MapsOwnerAndStatus()
    {
        // Arrange
        var portfolio = PropertyPortfolio.Create(HomeownerOwner);

        // Act
        var resource = PropertyPortfolioResourceFromEntityAssembler.ToResourceFromEntity(portfolio);

        // Assert
        Assert.Equal((Homeowner, "Empty", 0), (resource.OwnerId, resource.Status, resource.Entries.Count));
    }

    [Fact]
    public void PropertyResource_ToResourceFromEntity_MapsAddressAndGeolocation()
    {
        // Arrange
        var property = NewProperty();

        // Act
        var resource = PropertyResourceFromEntityAssembler.ToResourceFromEntity(property);

        // Assert
        Assert.Equal(("Main", "Lima", -12.1, "gps"), (resource.Address.Street, resource.Address.City, resource.Geolocation.Latitude,
            resource.Geolocation.Source));
    }

    [Fact]
    public void PropertyResource_ToResourceFromEntity_MapsStatusAndType()
    {
        // Arrange
        var property = NewProperty();

        // Act
        var resource = PropertyResourceFromEntityAssembler.ToResourceFromEntity(property);

        // Assert
        Assert.Equal((Homeowner, "Created", true, "Residential", (string?)null),
            (resource.OwnerId, resource.Status, resource.IsActive, resource.PropertyType, resource.MainPhotoProviderId));
    }

    [Fact]
    public async Task PropertyResource_ToResourceFromEntityWithPhoto_MapsPhotos()
    {
        // Arrange
        var property = NewProperty();
        var properties = new Fake<IPropertyRepository>().Returns(nameof(IPropertyRepository.FindByIdAsync), property);
        await new PropertyCommandService(properties.Object, new Fake<IUnitOfWork>().Object, new Fake<IMediator>().Object,
            new Fake<IFileStorageService>().Object, new Fake<IOrphanedFileRepository>().Object)
            .Handle(new AddPhotoToPropertyCommand(property.Id, "https://img/1.jpg"));

        // Act
        var resource = PropertyResourceFromEntityAssembler.ToResourceFromEntity(property);

        // Assert
        Assert.Equal("https://img/1.jpg", Assert.Single(resource.Photos).PublicUrl);
    }
}

public class TechnicianInventoryResourceFromEntityAssemblerTests
{
    private static readonly TechnicianId Technician = TechnicianId.From(Ids.Technician);
    private readonly Component _component = Component.Create("Breaker", "16A", true);

    private async Task<TechnicianInventory> InventoryWithStock()
    {
        var inventory = TechnicianInventory.Create(Technician);
        var type = ComponentType.Create("Protection", "Breakers");
        await new TechnicianInventoryCommandService(
                new Fake<ITechnicianInventoryRepository>().Returns(nameof(ITechnicianInventoryRepository.FindByTechnicianIdAsync), inventory).Object,
                new Fake<IComponentRepository>().Returns(nameof(IComponentRepository.FindByIdAsync), _component).Object,
                new Fake<IComponentTypeRepository>().Returns(nameof(IComponentTypeRepository.FindByIdAsync), type).Object,
                new Fake<IUnitOfWork>().Object, new Fake<IMediator>().Object)
            .Handle(new AddStockToInventoryCommand(Technician, _component.Id, type.Id, 6, 2));
        return inventory;
    }

    [Fact]
    public async Task ToResourceFromReadModel_WithKnownComponent_UsesComponentName()
    {
        // Arrange
        var inventory = await InventoryWithStock();
        var readModel = new TechnicianInventoryReadModel(inventory, new Dictionary<string, string> { [_component.Id.Value] = "Breaker" });

        // Act
        var resource = TechnicianInventoryResourceFromEntityAssembler.ToResourceFromReadModel(readModel);

        // Assert
        var item = Assert.Single(resource.StockItems);
        Assert.Equal(("Breaker", 6, 2), (item.ComponentName, item.QuantityAvailable, item.AlertThreshold));
    }

    [Fact]
    public async Task ToResourceFromReadModel_WithUnknownComponent_UsesFallbackName()
    {
        // Arrange
        var readModel = new TechnicianInventoryReadModel(await InventoryWithStock(), new Dictionary<string, string>());

        // Act
        var resource = TechnicianInventoryResourceFromEntityAssembler.ToResourceFromReadModel(readModel);

        // Assert
        Assert.Equal("Unknown Component", Assert.Single(resource.StockItems).ComponentName);
    }

    [Fact]
    public async Task ToResourceFromEntity_WithoutNames_UsesFallbackName()
    {
        // Arrange
        var inventory = await InventoryWithStock();

        // Act
        var resource = TechnicianInventoryResourceFromEntityAssembler.ToResourceFromEntity(inventory);

        // Assert
        Assert.Equal((Ids.Technician, "Unknown Component"), (resource.TechnicianId, Assert.Single(resource.StockItems).ComponentName));
    }

    [Fact]
    public async Task ToResourceFromEntity_WithNames_UsesProvidedName()
    {
        // Arrange
        var inventory = await InventoryWithStock();

        // Act
        var resource = TechnicianInventoryResourceFromEntityAssembler.ToResourceFromEntity(inventory,
            new Dictionary<string, string> { [_component.Id.Value] = "Breaker 16A" });

        // Assert
        Assert.Equal("Breaker 16A", Assert.Single(resource.StockItems).ComponentName);
    }
}
