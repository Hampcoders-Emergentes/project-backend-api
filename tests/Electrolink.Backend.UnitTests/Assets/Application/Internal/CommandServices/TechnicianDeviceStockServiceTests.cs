using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Assets.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using static Electrolink.Backend.UnitTests.Assets.AssetsTestData;

namespace Electrolink.Backend.UnitTests.Assets.Application.Internal.CommandServices;

public class TechnicianDeviceStockServiceTests
{
    private readonly Fake<IUserQueryService> _users = new();
    private readonly Fake<IProfilesContextFacade> _profiles = TechnicianProfiles();
    private readonly Fake<ITechnicianInventoryRepository> _inventories = new();
    private readonly Fake<IIoTDeviceRepository> _devices = new();
    private readonly Fake<IIoTDeviceCommandService> _commands = new();

    public TechnicianDeviceStockServiceTests()
    {
        _users.Returns(nameof(IUserQueryService.Handle), User.Create(Email.From("tech@electrolink.pe"), HashedPassword.FromHash("h"), AccessRole.User));
        _inventories.Returns(nameof(ITechnicianInventoryRepository.FindByTechnicianIdAsync),
            TechnicianInventory.Create(TechnicianId.From(Ids.Technician)));
    }

    private TechnicianDeviceStockService CreateSut() =>
        new(_users.Object, _profiles.Object, _inventories.Object, _devices.Object, _commands.Object);

    [Fact]
    public async Task Register_ForEligibleTechnician_DelegatesRegistrationWithTechnician()
    {
        // Arrange
        var device = InStockDevice(Ids.Technician);
        _commands.Returns(nameof(IIoTDeviceCommandService.Handle), device);
        var sut = CreateSut();

        // Act
        var result = await sut.Register(Ids.Actor, "SN-1", "key", "1.0");

        // Assert
        Assert.Same(device, result);
        Assert.Equal(Ids.Technician, _commands.Calls.Single().Arg<RegisterTechnicianIoTDeviceCommand>().Technician.Value);
    }

    [Fact]
    public async Task Register_WhenUserIsSuspended_ThrowsTechnicianStockAccessDeniedException()
    {
        // Arrange
        var user = User.Create(Email.From("tech@electrolink.pe"), HashedPassword.FromHash("h"), AccessRole.User);
        user.Suspend("abuse");
        _users.Returns(nameof(IUserQueryService.Handle), user);
        var sut = CreateSut();

        // Act
        var act = () => sut.Register(Ids.Actor, "SN-1", "key", "1.0");

        // Assert
        await Assert.ThrowsAsync<TechnicianStockAccessDeniedException>(act);
    }

    [Fact]
    public async Task Register_WhenProfileIsNotTechnician_ThrowsTechnicianStockAccessDeniedException()
    {
        // Arrange
        _profiles.Returns(nameof(IProfilesContextFacade.GetProfileClaimsAsync), Claims("HomeOwner", Homeowner));
        var sut = CreateSut();

        // Act
        var act = () => sut.Register(Ids.Actor, "SN-1", "key", "1.0");

        // Assert
        await Assert.ThrowsAsync<TechnicianStockAccessDeniedException>(act);
    }

    [Fact]
    public async Task Register_WhenTechnicianHasNoInventory_ThrowsTechnicianStockAccessDeniedException()
    {
        // Arrange
        _inventories.Returns(nameof(ITechnicianInventoryRepository.FindByTechnicianIdAsync), null);
        var sut = CreateSut();

        // Act
        var act = () => sut.Register(Ids.Actor, "SN-1", "key", "1.0");

        // Assert
        await Assert.ThrowsAsync<TechnicianStockAccessDeniedException>(act);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task GetStock_WithInvalidPaging_ThrowsArgumentOutOfRangeException(int page, int pageSize)
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.GetStock(Ids.Actor, page, pageSize);

        // Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(act);
    }

    [Fact]
    public async Task GetStock_WithValidPaging_ReturnsRepositoryPage()
    {
        // Arrange
        var page = new TechnicianDeviceStockPage([], 0, 0);
        _devices.Returns(nameof(IIoTDeviceRepository.GetTechnicianStockPageAsync), page);
        var sut = CreateSut();

        // Act
        var result = await sut.GetStock(Ids.Actor, 1, 10);

        // Assert
        Assert.Same(page, result);
    }

    [Fact]
    public async Task GetDevice_OwnedByTechnician_ReturnsDevice()
    {
        // Arrange
        var device = InStockDevice(Ids.Technician);
        _devices.Returns(nameof(IIoTDeviceRepository.FindByIdAsync), device);
        var sut = CreateSut();

        // Act
        var result = await sut.GetDevice(Ids.Actor, device.Id.Value);

        // Assert
        Assert.Same(device, result);
    }

    [Fact]
    public async Task GetDevice_OwnedByAnotherTechnician_ReturnsNull()
    {
        // Arrange
        var device = InStockDevice("tech-other");
        _devices.Returns(nameof(IIoTDeviceRepository.FindByIdAsync), device);
        var sut = CreateSut();

        // Act
        var result = await sut.GetDevice(Ids.Actor, device.Id.Value);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetLegacyActiveCount_ForOtherOwner_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.GetLegacyActiveCount(Ids.Actor, "tech-other");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetLegacyActiveCount_ForOwnTechnician_ReturnsActiveDeviceCount()
    {
        // Arrange
        _devices.Returns(nameof(IIoTDeviceRepository.CountTechnicianActiveDevicesAsync), 4);
        var sut = CreateSut();

        // Act
        var result = await sut.GetLegacyActiveCount(Ids.Actor, Ids.Technician);

        // Assert
        Assert.Equal(4, result);
    }

    [Fact]
    public async Task GetLegacyStock_ForEligibleTechnician_ReturnsRepositoryDevices()
    {
        // Arrange
        var device = InStockDevice(Ids.Technician);
        _devices.Returns(nameof(IIoTDeviceRepository.FindTechnicianStockAsync), new[] { device });
        var sut = CreateSut();

        // Act
        var result = await sut.GetLegacyStock(Ids.Actor);

        // Assert
        Assert.Same(device, Assert.Single(result));
    }
}
