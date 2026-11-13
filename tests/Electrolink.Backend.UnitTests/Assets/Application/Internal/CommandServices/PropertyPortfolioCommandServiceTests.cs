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
using static Electrolink.Backend.UnitTests.Assets.AssetsTestData;

namespace Electrolink.Backend.UnitTests.Assets.Application.Internal.CommandServices;

public class PropertyPortfolioCommandServiceTests
{
    private readonly Fake<IPropertyPortfolioRepository> _portfolios = new();
    private readonly Fake<IPropertyRepository> _properties = new();
    private readonly Fake<IMediator> _mediator = new();
    private readonly Property _property = NewProperty();

    private PropertyPortfolioCommandService CreateSut() => new(_portfolios.Object, _properties.Object, new Fake<IUnitOfWork>().Object,
        _mediator.Object, NullLogger<PropertyPortfolioCommandService>.Instance);

    private PropertyPortfolio GivenPortfolio()
    {
        var portfolio = PropertyPortfolio.Create(HomeownerOwner);
        portfolio.ClearDomainEvents();
        _portfolios.Returns(nameof(IPropertyPortfolioRepository.FindByOwnerWithEntriesAsync), portfolio);
        return portfolio;
    }

    private AddPropertyToPortfolioCommand AddCommand(ClientIdentity? owner = null) =>
        new(owner ?? HomeownerOwner, _property.Id, "Home", true, "ownerOccupied");

    [Fact]
    public async Task Handle_CreateWhenOwnerHasPortfolio_ThrowsDuplicateAssetException()
    {
        // Arrange
        _portfolios.Returns(nameof(IPropertyPortfolioRepository.FindByOwnerAsync), PropertyPortfolio.Create(HomeownerOwner));
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new CreatePropertyPortfolioCommand(HomeownerOwner));

        // Assert
        await Assert.ThrowsAsync<DuplicateAssetException>(act);
    }

    [Fact]
    public async Task Handle_CreateForNewOwner_ReturnsEmptyPortfolio()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new CreatePropertyPortfolioCommand(HomeownerOwner));

        // Assert
        Assert.Equal((HomeownerOwner, EPortfolioStatus.Empty), (result!.Owner, result.Status));
        Assert.Equal(1, _mediator.CallCount(nameof(IMediator.Publish)));
    }

    [Fact]
    public async Task Handle_AddWhenPortfolioMissing_ThrowsAssetNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(AddCommand());

        // Assert
        await Assert.ThrowsAsync<AssetNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_AddWhenPropertyMissing_ThrowsAssetNotFoundException()
    {
        // Arrange
        GivenPortfolio();
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(AddCommand());

        // Assert
        await Assert.ThrowsAsync<AssetNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_AddPropertyOfAnotherOwner_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        GivenPortfolio();
        _properties.Returns(nameof(IPropertyRepository.FindByIdAsync), NewProperty(ClientIdentity.FromCompany(Company)));
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(AddCommand());

        // Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(act);
    }

    [Fact]
    public async Task Handle_AddOwnedProperty_AddsEntryAndMarksPropertyInPortfolio()
    {
        // Arrange
        GivenPortfolio();
        _properties.Returns(nameof(IPropertyRepository.FindByIdAsync), _property);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(AddCommand());

        // Assert
        Assert.Equal(EOccupancyStatus.OwnerOccupied, Assert.Single(result!.Entries).OccupancyStatus);
        Assert.Equal(EPropertyStatus.InPortfolio, _property.Status);
    }

    [Fact]
    public async Task Handle_AddWithUnknownOccupancy_ThrowsArgumentException()
    {
        // Arrange
        GivenPortfolio();
        _properties.Returns(nameof(IPropertyRepository.FindByIdAsync), _property);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(AddCommand() with { OccupancyStatus = "squatted" });

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_RemoveWhenPortfolioMissing_ThrowsAssetNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new RemovePropertyFromPortfolioCommand(HomeownerOwner, _property.Id, "sold"));

        // Assert
        await Assert.ThrowsAsync<AssetNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_RemovePropertyNotInPortfolio_ThrowsKeyNotFoundException()
    {
        // Arrange
        GivenPortfolio();
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new RemovePropertyFromPortfolioCommand(HomeownerOwner, _property.Id, "sold"));

        // Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_RemoveListedProperty_MarksPropertyAvailable()
    {
        // Arrange
        var portfolio = GivenPortfolio();
        _properties.Returns(nameof(IPropertyRepository.FindByIdAsync), _property);
        var sut = CreateSut();
        await sut.Handle(AddCommand());

        // Act
        var result = await sut.Handle(new RemovePropertyFromPortfolioCommand(HomeownerOwner, _property.Id, "sold"));

        // Assert
        Assert.True(result);
        Assert.Empty(portfolio.Entries);
        Assert.Equal(EPropertyStatus.Created, _property.Status);
    }
}
