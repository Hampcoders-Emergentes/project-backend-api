using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Assets.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using static Electrolink.Backend.UnitTests.Assets.AssetsTestData;

namespace Electrolink.Backend.UnitTests.Assets.Interfaces.REST.Transform;

public class AssetsCommandAssemblersTests
{
    private static readonly string Property = PropertyId.NewPropertyId().Value;
    private static readonly string ComponentValue = ComponentId.NewComponentId().Value;
    private static readonly string TypeValue = ComponentTypeId.NewComponentTypeId().Value;
    private static readonly string Assignment = AssignmentId.NewAssignmentId().Value;

    private static AddressResource AddressResource() => new("Main", "100", "Miraflores", "Lima", "PE", "15074");

    [Fact]
    public void AddPropertyToPortfolio_ToCommandFromResource_MapsOwnerAndEntryData()
    {
        // Arrange
        var resource = new AddPropertyToPortfolioResource(Property, "Home", true, "Rented");

        // Act
        var command = AddPropertyToPortfolioCommandFromResourceAssembler.ToCommandFromResource(resource, Homeowner);

        // Assert
        Assert.Equal(new AddPropertyToPortfolioCommand(HomeownerOwner, PropertyId.From(Property), "Home", true, "Rented"), command);
    }

    [Fact]
    public void AddPropertyToPortfolio_ToCommandFromResourceWithUnknownOwnerPrefix_ThrowsInvalidIdException()
    {
        // Arrange
        var resource = new AddPropertyToPortfolioResource(Property, "Home", true, "Rented");

        // Act
        var act = () => AddPropertyToPortfolioCommandFromResourceAssembler.ToCommandFromResource(resource, "user-1");

        // Assert
        Assert.Throws<InvalidIdException>(act);
    }

    [Fact]
    public void AddStockToInventory_ToCommandFromResource_MapsIdsAndQuantities()
    {
        // Arrange
        var resource = new AddStockToInventoryResource(ComponentValue, TypeValue, 7, 2);

        // Act
        var command = AddStockToInventoryCommandFromResourceAssembler.ToCommandFromResource(resource, Ids.Technician);

        // Assert
        Assert.Equal(new AddStockToInventoryCommand(TechnicianId.From(Ids.Technician), ComponentId.From(ComponentValue),
            ComponentTypeId.From(TypeValue), 7, 2), command);
    }

    [Fact]
    public void ArchiveProperty_ToCommandFromResource_MapsIdAndReason()
    {
        // Act
        var command = ArchivePropertyCommandFromResourceAssembler.ToCommandFromResource(Property, "sold");

        // Assert
        Assert.Equal(new ArchivePropertyCommand(PropertyId.From(Property), "sold"), command);
    }

    [Fact]
    public void ArchiveProperty_ToCommandFromResourceWithMalformedId_ThrowsInvalidIdException()
    {
        // Act
        var act = () => ArchivePropertyCommandFromResourceAssembler.ToCommandFromResource("house-1", "sold");

        // Assert
        Assert.Throws<InvalidIdException>(act);
    }

    [Fact]
    public void ConsumeComponentsForService_ToCommandFromResource_MapsTechnicianAndAssignment()
    {
        // Act
        var command = ConsumeComponentsForServiceCommandFromResourceAssembler.ToCommandFromResource(Ids.Technician, Assignment);

        // Assert
        Assert.Equal(new ConsumeComponentsForServiceCommand(TechnicianId.From(Ids.Technician), AssignmentId.From(Assignment)), command);
    }

    [Fact]
    public void CreateComponent_ToCommandFromResource_MapsAllFields()
    {
        // Act
        var command = CreateComponentCommandFromResourceAssembler.ToCommandFromResource(new CreateComponentResource("Breaker", "16A", false));

        // Assert
        Assert.Equal(new CreateComponentCommand("Breaker", "16A", false), command);
    }

    [Fact]
    public void CreateComponentType_ToCommandFromResource_MapsAllFields()
    {
        // Act
        var command = CreateComponentTypeCommandFromResourceAssembler.ToCommandFromResource(new CreateComponentTypeResource("Sensors", "IoT"));

        // Assert
        Assert.Equal(new CreateComponentTypeCommand("Sensors", "IoT"), command);
    }

    [Fact]
    public void CreateProperty_ToCommandFromResource_UsesManualGeolocationSource()
    {
        // Arrange
        var resource = new CreatePropertyResource(AddressResource(), new GeolocationResource(-12.1, -77.0, 5, "gps"), "Commercial");

        // Act
        var command = CreatePropertyCommandFromResourceAssembler.ToCommandFromResource(resource, Company);

        // Assert
        Assert.Equal(("MANUAL", EPropertyType.Commercial, ClientIdentity.FromCompany(Company)),
            (command.Geolocation.Source, command.PropertyType, command.Owner));
    }

    [Fact]
    public void CreateProperty_ToCommandFromResourceWithUnknownType_DefaultsToResidential()
    {
        // Arrange
        var resource = new CreatePropertyResource(AddressResource(), new GeolocationResource(-12.1, -77.0, 5, "gps"), "castle");

        // Act
        var command = CreatePropertyCommandFromResourceAssembler.ToCommandFromResource(resource, Homeowner);

        // Assert
        Assert.Equal(EPropertyType.Residential, command.PropertyType);
    }

    [Fact]
    public void CreateProperty_ToCommandFromResourceWithInvalidLatitude_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var resource = new CreatePropertyResource(AddressResource(), new GeolocationResource(95, -77.0, 5, "gps"), "Residential");

        // Act
        var act = () => CreatePropertyCommandFromResourceAssembler.ToCommandFromResource(resource, Homeowner);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Fact]
    public void DecreaseStock_ToCommandFromResource_MapsAmount()
    {
        // Act
        var command = DecreaseStockCommandFromResourceAssembler.ToCommandFromResource(new AdjustStockAmountResource(3), Ids.Technician, ComponentValue);

        // Assert
        Assert.Equal(new DecreaseStockCommand(TechnicianId.From(Ids.Technician), ComponentId.From(ComponentValue), 3), command);
    }

    [Fact]
    public void IncreaseStock_ToCommandFromResource_MapsAmount()
    {
        // Act
        var command = IncreaseStockCommandFromResourceAssembler.ToCommandFromResource(new AdjustStockAmountResource(3), Ids.Technician, ComponentValue);

        // Assert
        Assert.Equal(new IncreaseStockCommand(TechnicianId.From(Ids.Technician), ComponentId.From(ComponentValue), 3), command);
    }

    [Fact]
    public void GetPropertyPhotoUploadUrl_ToCommand_MapsOwnerAndProperty()
    {
        // Act
        var command = GetPropertyPhotoUploadUrlCommandFromResourceAssembler.ToCommand(Homeowner, Property);

        // Assert
        Assert.Equal(new GetPropertyPhotoUploadUrlCommand(HomeownerOwner, PropertyId.From(Property)), command);
    }

    [Fact]
    public void RecordInstallation_ToCommandFromResource_LeavesInstallationRequestEmpty()
    {
        // Arrange
        var installedAt = DateTime.UtcNow;

        // Act
        var command = RecordInstallationCommandFromResourceAssembler.ToCommandFromResource("dev-1", Property,
            new RecordDeviceInstallationResource(Ids.Technician, "2.0", installedAt));

        // Assert
        Assert.Equal(new RecordDeviceInstallationCommand("dev-1", Property, Ids.Technician, null, "2.0", installedAt), command);
    }

    [Fact]
    public void RecordMaintenanceForProperty_ToCommandFromResource_MapsAllFields()
    {
        // Arrange
        var completedAt = DateTime.UtcNow;

        // Act
        var command = RecordMaintenanceForPropertyCommandFromResourceAssembler.ToCommandFromResource(
            new RecordMaintenanceResource(Assignment, Ids.Technician, "fixed", completedAt), Property);

        // Assert
        Assert.Equal(new RecordMaintenanceForPropertyCommand(PropertyId.From(Property), AssignmentId.From(Assignment),
            TechnicianId.From(Ids.Technician), "fixed", completedAt), command);
    }

    [Fact]
    public void RegisterIoTDevice_ToCommandFromResource_MapsAllFields()
    {
        // Act
        var command = RegisterIoTDeviceCommandFromResourceAssembler.ToCommandFromResource(new RegisterIoTDeviceResource("SN-1", "key", "1.0"));

        // Assert
        Assert.Equal(new RegisterIoTDeviceCommand("SN-1", "key", "1.0"), command);
    }

    [Fact]
    public void RegisterPropertyPhoto_ToCommand_MapsProviderAndUrl()
    {
        // Act
        var command = RegisterPropertyPhotoCommandFromResourceAssembler.ToCommand(Homeowner, Property,
            new RegisterPropertyPhotoResource("prov-1", "https://img/1.jpg"));

        // Assert
        Assert.Equal(new RegisterPropertyPhotoCommand(HomeownerOwner, PropertyId.From(Property), "prov-1", "https://img/1.jpg"), command);
    }

    [Fact]
    public void ReleaseReservation_ToCommandFromResource_MapsReason()
    {
        // Act
        var command = ReleaseReservationCommandFromResourceAssembler.ToCommandFromResource(Ids.Technician, Assignment, "cancelled");

        // Assert
        Assert.Equal(new ReleaseReservationCommand(TechnicianId.From(Ids.Technician), AssignmentId.From(Assignment), "cancelled"), command);
    }

    [Fact]
    public void RemoveComponentStock_ToCommandFromResource_MapsIds()
    {
        // Act
        var command = RemoveComponentStockCommandAssembler.ToCommandFromResource(Ids.Technician, ComponentValue);

        // Assert
        Assert.Equal(new RemoveComponentStockCommand(TechnicianId.From(Ids.Technician), ComponentId.From(ComponentValue)), command);
    }

    [Fact]
    public void RemovePropertyFromPortfolio_ToCommandFromResource_MapsOwnerAndReason()
    {
        // Act
        var command = RemovePropertyFromPortfolioCommandFromResourceAssembler.ToCommandFromResource(Homeowner, Property, "sold");

        // Assert
        Assert.Equal(new RemovePropertyFromPortfolioCommand(HomeownerOwner, PropertyId.From(Property), "sold"), command);
    }

    [Fact]
    public void ReserveComponents_ToCommandFromResource_MapsEveryComponent()
    {
        // Arrange
        var resource = new ReserveComponentsResource(Assignment, [new(ComponentValue, 2)]);

        // Act
        var command = ReserveComponentsCommandFromResourceAssembler.ToCommandFromResource(resource, Ids.Technician);

        // Assert
        Assert.Equal(new ComponentAdjustment(ComponentId.From(ComponentValue), 2), Assert.Single(command.ComponentsToReserve));
    }

    [Fact]
    public void ReserveComponents_ToCommandFromResourceWithZeroQuantity_ThrowsArgumentException()
    {
        // Arrange
        var resource = new ReserveComponentsResource(Assignment, [new(ComponentValue, 0)]);

        // Act
        var act = () => ReserveComponentsCommandFromResourceAssembler.ToCommandFromResource(resource, Ids.Technician);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void SetPropertyMainPhoto_ToCommand_MapsProvider()
    {
        // Act
        var command = SetPropertyMainPhotoCommandFromResourceAssembler.ToCommand(Homeowner, Property, new SetPropertyMainPhotoResource("prov-1"));

        // Assert
        Assert.Equal(new SetPropertyMainPhotoCommand(HomeownerOwner, PropertyId.From(Property), "prov-1"), command);
    }

    [Fact]
    public void UpdateComponent_ToCommandFromResource_MapsAllFields()
    {
        // Act
        var command = UpdateComponentCommandFromResourceAssembler.ToCommandFromResource(new UpdateComponentResource("New", null, true), ComponentValue);

        // Assert
        Assert.Equal(new UpdateComponentCommand(ComponentId.From(ComponentValue), "New", null, true), command);
    }

    [Fact]
    public void UpdateComponent_ToCommandFromNullResource_ThrowsArgumentNullException()
    {
        // Act
        var act = () => UpdateComponentCommandFromResourceAssembler.ToCommandFromResource(null!, ComponentValue);

        // Assert
        Assert.Throws<ArgumentNullException>(act);
    }

    [Fact]
    public void UpdateComponentStock_ToCommandFromResource_MapsQuantities()
    {
        // Act
        var command = UpdateComponentStockCommandFromResourceAssembler.ToCommandFromResource(new UpdateComponentStockResource(9, 3),
            Ids.Technician, ComponentValue);

        // Assert
        Assert.Equal(new UpdateComponentStockCommand(TechnicianId.From(Ids.Technician), ComponentId.From(ComponentValue), 9, 3), command);
    }

    [Fact]
    public void UpdateComponentStock_ToCommandFromNullResource_ThrowsArgumentNullException()
    {
        // Act
        var act = () => UpdateComponentStockCommandFromResourceAssembler.ToCommandFromResource(null!, Ids.Technician, ComponentValue);

        // Assert
        Assert.Throws<ArgumentNullException>(act);
    }

    [Fact]
    public void UpdateComponentType_ToCommandFromResource_MapsNameAndDescription()
    {
        // Act
        var command = UpdateComponentTypeCommandFromResourceAssembler.ToCommandFromResource(new UpdateComponentTypeResource("Relays", "d", true), TypeValue);

        // Assert
        Assert.Equal(new UpdateComponentTypeCommand(ComponentTypeId.From(TypeValue), "Relays", "d"), command);
    }

    [Fact]
    public void UpdateComponentType_ToCommandFromNullResource_ThrowsArgumentNullException()
    {
        // Act
        var act = () => UpdateComponentTypeCommandFromResourceAssembler.ToCommandFromResource(null!, TypeValue);

        // Assert
        Assert.Throws<ArgumentNullException>(act);
    }

    [Fact]
    public void UpdateProperty_ToCommandFromResource_MapsOwnerAndTrimmedAddress()
    {
        // Arrange
        var resource = new UpdatePropertyResource(Homeowner, new AddressResource(" Main ", "100", "Miraflores", "Lima", "PE", "15074"));

        // Act
        var command = UpdatePropertyCommandFromResourceAssembler.ToCommandFromResource(resource, Property);

        // Assert
        Assert.Equal((HomeownerOwner, "Main"), (command.Owner, command.Address.Street));
    }

    [Fact]
    public void UpdatePropertyLocation_ToCommandFromResourceWithoutParts_LeavesBothNull()
    {
        // Act
        var command = UpdatePropertyLocationCommandFromResourceAssembler.ToCommandFromResource(new UpdateLocationResource(null, null), Property);

        // Assert
        Assert.Equal((null, null), (command.Address, command.Geolocation));
    }

    [Fact]
    public void UpdatePropertyLocation_ToCommandFromResourceWithGeolocation_KeepsProvidedSource()
    {
        // Arrange
        var resource = new UpdateLocationResource(null, new GeolocationResource(-12, -77, 3, "gps"));

        // Act
        var command = UpdatePropertyLocationCommandFromResourceAssembler.ToCommandFromResource(resource, Property);

        // Assert
        Assert.Equal(("gps", -12d), (command.Geolocation!.Source, command.Geolocation.Latitude));
    }

    [Fact]
    public void UpdatePropertyLocation_ToCommandFromResourceWithAddress_MapsAddress()
    {
        // Arrange
        var resource = new UpdateLocationResource(AddressResource(), null);

        // Act
        var command = UpdatePropertyLocationCommandFromResourceAssembler.ToCommandFromResource(resource, Property);

        // Assert
        Assert.Equal(Address(), command.Address);
    }
}
