using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Assets.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Entities;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Infrastructure;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces;
using MediatR;
using static Electrolink.Backend.UnitTests.Assets.AssetsTestData;

namespace Electrolink.Backend.UnitTests.Assets.Application.Internal.CommandServices;

public class PropertyCommandServiceTests
{
    private readonly Fake<IPropertyRepository> _properties = new();
    private readonly Fake<IUnitOfWork> _unitOfWork = new();
    private readonly Fake<IMediator> _mediator = new();
    private readonly Fake<IFileStorageService> _storage = new();
    private readonly Fake<IOrphanedFileRepository> _orphans = new();
    private readonly Property _property = NewProperty();

    private PropertyCommandService CreateSut() =>
        new(_properties.Object, _unitOfWork.Object, _mediator.Object, _storage.Object, _orphans.Object);

    private void GivenPropertyById() => _properties.Returns(nameof(IPropertyRepository.FindByIdAsync), _property);
    private void GivenOwnedProperty() => _properties.Returns(nameof(IPropertyRepository.FindByIdAndOwnerAsync), _property);

    [Fact]
    public async Task Handle_Create_PersistsPropertyForOwner()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new CreatePropertyCommand(HomeownerOwner, Address(), Location, EPropertyType.Commercial));

        // Assert
        Assert.Equal((HomeownerOwner, EPropertyType.Commercial), (result!.Owner, result.PropertyType));
        Assert.Equal(1, _properties.CallCount(nameof(IPropertyRepository.AddAsync)));
    }

    [Fact]
    public async Task Handle_Create_PublishesCreatedEventAndClearsEvents()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new CreatePropertyCommand(HomeownerOwner, Address(), Location, EPropertyType.Residential));

        // Assert
        Assert.Equal(1, _mediator.CallCount(nameof(IMediator.Publish)));
        Assert.Empty(result!.DomainEvents);
    }

    [Fact]
    public async Task Handle_AddPhotoWhenPropertyMissing_ThrowsAssetNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new AddPhotoToPropertyCommand(_property.Id, "https://img/1.jpg"));

        // Assert
        await Assert.ThrowsAsync<AssetNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_AddPhotoWithUrl_AddsPhotoToProperty()
    {
        // Arrange
        GivenPropertyById();
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new AddPhotoToPropertyCommand(_property.Id, "https://img/1.jpg"));

        // Assert
        Assert.Equal("https://img/1.jpg", Assert.Single(result!.Photos).PublicUrl);
    }

    [Fact]
    public async Task Handle_GetUploadUrlForNotOwnedProperty_ThrowsAssetNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new GetPropertyPhotoUploadUrlCommand(HomeownerOwner, _property.Id));

        // Assert
        await Assert.ThrowsAsync<AssetNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_GetUploadUrlForOwnedProperty_ReturnsSignedUpload()
    {
        // Arrange
        GivenOwnedProperty();
        var signed = new SignedUploadData("https://upload", "sig", 1, "key");
        _storage.Returns(nameof(IFileStorageService.GetSignedUploadUrlForPropertyPhotoAsync), signed);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetPropertyPhotoUploadUrlCommand(HomeownerOwner, _property.Id));

        // Assert
        Assert.Same(signed, result);
    }

    [Fact]
    public async Task Handle_RegisterPhotoForOwnedProperty_AddsProviderPhoto()
    {
        // Arrange
        GivenOwnedProperty();
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new RegisterPropertyPhotoCommand(HomeownerOwner, _property.Id, "prov-1", "https://img/1.jpg"));

        // Assert
        Assert.Equal("prov-1", Assert.Single(result!.Photos).ProviderId);
    }

    [Fact]
    public async Task Handle_RegisterPhotoWhenSaveFails_SchedulesOrphanedFileDeletionAndRethrows()
    {
        // Arrange
        GivenOwnedProperty();
        var attempts = 0;
        _unitOfWork.On(nameof(IUnitOfWork.CompleteAsync), _ => ++attempts == 1 ? throw new InvalidOperationException("db down") : null);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new RegisterPropertyPhotoCommand(HomeownerOwner, _property.Id, "prov-1", "https://img/1.jpg"));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal("prov-1", _orphans.Calls.Single().Arg<OrphanedFileDeletion>().ProviderId);
    }

    [Fact]
    public async Task Handle_SetMainPhotoWithUnknownPhoto_ThrowsKeyNotFoundException()
    {
        // Arrange
        GivenOwnedProperty();
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new SetPropertyMainPhotoCommand(HomeownerOwner, _property.Id, "missing"));

        // Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_SetMainPhotoWithRegisteredPhoto_SetsMainPhoto()
    {
        // Arrange
        GivenOwnedProperty();
        var sut = CreateSut();
        await sut.Handle(new RegisterPropertyPhotoCommand(HomeownerOwner, _property.Id, "prov-1", "https://img/1.jpg"));

        // Act
        var result = await sut.Handle(new SetPropertyMainPhotoCommand(HomeownerOwner, _property.Id, "prov-1"));

        // Assert
        Assert.Equal("prov-1", result!.MainPhotoProviderId);
    }

    [Fact]
    public async Task Handle_UpdateLocationWithAddressOnly_KeepsGeolocation()
    {
        // Arrange
        GivenPropertyById();
        var originalLocation = _property.Geolocation;
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new UpdatePropertyLocationCommand(_property.Id, Address("Second"), null));

        // Assert
        Assert.Equal(("Second", originalLocation), (result!.Address.Street, result.Geolocation));
    }

    [Fact]
    public async Task Handle_UpdateLocationWhenPropertyMissing_ThrowsAssetNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new UpdatePropertyLocationCommand(_property.Id, Address("Second"), null));

        // Assert
        await Assert.ThrowsAsync<AssetNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_DeactivateWhenPropertyMissing_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new DeactivatePropertyCommand(_property.Id));

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_DeactivateActiveProperty_MarksPropertyInactive()
    {
        // Arrange
        GivenPropertyById();
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new DeactivatePropertyCommand(_property.Id));

        // Assert
        Assert.False(result!.IsActive);
    }

    [Fact]
    public async Task Handle_ActivateInactiveProperty_MarksPropertyActive()
    {
        // Arrange
        GivenPropertyById();
        var sut = CreateSut();
        await sut.Handle(new DeactivatePropertyCommand(_property.Id));

        // Act
        var result = await sut.Handle(new ActivatePropertyCommand(_property.Id));

        // Assert
        Assert.True(result!.IsActive);
    }

    [Fact]
    public async Task Handle_ActivateWhenPropertyMissing_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new ActivatePropertyCommand(_property.Id));

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_ArchivePropertyWithoutDevices_MarksPropertyArchived()
    {
        // Arrange
        GivenPropertyById();
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new ArchivePropertyCommand(_property.Id, "sold"));

        // Assert
        Assert.Equal((EPropertyStatus.Archived, false), (result!.Status, result.IsActive));
    }

    [Fact]
    public async Task Handle_ArchiveWhenPropertyMissing_ThrowsAssetNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new ArchivePropertyCommand(_property.Id, "sold"));

        // Assert
        await Assert.ThrowsAsync<AssetNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_UpdateNotOwnedProperty_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new UpdatePropertyCommand(_property.Id, HomeownerOwner, Address("Second")));

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_UpdateOwnedProperty_ReplacesAddress()
    {
        // Arrange
        GivenOwnedProperty();
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new UpdatePropertyCommand(_property.Id, HomeownerOwner, Address("Second")));

        // Assert
        Assert.Equal("Second", result!.Address.Street);
    }

    [Fact]
    public async Task Handle_DeleteWhenPropertyMissing_ReturnsFalse()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new DeletePropertyCommand(_property.Id));

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task Handle_DeleteExistingProperty_RemovesProperty()
    {
        // Arrange
        GivenPropertyById();
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new DeletePropertyCommand(_property.Id));

        // Assert
        Assert.True(result);
        Assert.Same(_property, _properties.CallsTo(nameof(IPropertyRepository.Remove)).Single().Arg<Property>());
    }

    [Fact]
    public async Task Handle_RecordMaintenanceWhenPropertyMissing_ThrowsAssetNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new RecordMaintenanceForPropertyCommand(_property.Id, AssignmentId.NewAssignmentId(),
            TechnicianId.From(Ids.Technician), "fixed", DateTime.UtcNow));

        // Assert
        await Assert.ThrowsAsync<AssetNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_RecordMaintenanceOnProperty_PublishesMaintenanceEvent()
    {
        // Arrange
        GivenPropertyById();
        _property.ClearDomainEvents();
        var sut = CreateSut();

        // Act
        await sut.Handle(new RecordMaintenanceForPropertyCommand(_property.Id, AssignmentId.NewAssignmentId(),
            TechnicianId.From(Ids.Technician), "fixed", DateTime.UtcNow));

        // Assert
        Assert.Equal(1, _mediator.CallCount(nameof(IMediator.Publish)));
    }
}
