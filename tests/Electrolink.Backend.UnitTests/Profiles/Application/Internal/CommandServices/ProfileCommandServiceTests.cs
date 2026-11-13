using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.IAM.Interfaces.ACL;
using Hampcoders.Electrolink.API.Profiles.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Profiles.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Events;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Profiles.Domain.Repositories;
using Hampcoders.Electrolink.API.Profiles.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Entities;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Infrastructure;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using static Electrolink.Backend.UnitTests.Profiles.ProfilesTestData;

namespace Electrolink.Backend.UnitTests.Profiles.Application.Internal.CommandServices;

public class ProfileCommandServiceTests
{
    private readonly Fake<IProfileRepository> _profiles = new();
    private readonly Fake<IFileStorageService> _storage = new();
    private readonly Fake<IOrphanedFileRepository> _orphans = new();
    private readonly Fake<IMediator> _mediator = new();
    private readonly Fake<IProfileUniquenessChecker> _uniqueness = new();

    private ProfileCommandService CreateSut() => new(_profiles.Object, _storage.Object, _orphans.Object,
        new ExternalIamService(new Fake<IIamContextFacade>().Object), new Fake<IUnitOfWork>().Object, _mediator.Object, _uniqueness.Object,
        NullLogger<ProfileCommandService>.Instance);

    private Profile GivenByUser(Profile profile)
    {
        _profiles.Returns(nameof(IProfileRepository.FindByUserIdAsync), profile);
        return profile;
    }

    private Profile GivenById(Profile profile)
    {
        _profiles.Returns(nameof(IProfileRepository.FindByIdAsync), profile);
        return profile;
    }

    private Profile GivenByTechnician(Profile profile)
    {
        _profiles.Returns(nameof(IProfileRepository.FindByTechnicianIdAsync), profile);
        return profile;
    }

    [Fact]
    public async Task Handle_CreateForNewUser_PersistsIncompleteProfile()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var profile = await sut.Handle(new CreateProfileCommand(Ids.User));

        // Assert
        Assert.Equal(EProfileStatus.Incomplete, profile!.Status);
        Assert.Same(profile, _profiles.CallsTo(nameof(IProfileRepository.AddAsync)).Single().Arg<Profile>());
    }

    [Fact]
    public async Task Handle_CreateForExistingUser_ReturnsExistingProfileWithoutAdding()
    {
        // Arrange
        var existing = GivenByUser(Incomplete());
        _profiles.Returns(nameof(IProfileRepository.ExistsByUserIdAsync), true);
        var sut = CreateSut();

        // Act
        var profile = await sut.Handle(new CreateProfileCommand(Ids.User));

        // Assert
        Assert.Same(existing, profile);
        Assert.Equal(0, _profiles.CallCount(nameof(IProfileRepository.AddAsync)));
    }

    [Fact]
    public async Task Handle_UpdateSpecialtiesForUnknownTechnician_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new UpdateTechnicianSpecialtiesCommand(Ids.Technician, ["Repair"]));

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_UpdateSpecialtiesWithUnknownNames_KeepsOnlyValidSpecialties()
    {
        // Arrange
        var profile = GivenByTechnician(Technician());
        var sut = CreateSut();

        // Act
        await sut.Handle(new UpdateTechnicianSpecialtiesCommand(profile.Technician!.TechnicianId.Value, ["repair", "juggling", "SolarInstallation"]));

        // Assert
        Assert.Equal([ESpecialty.Repair, ESpecialty.SolarInstallation], profile.Technician.Specialties);
    }

    [Fact]
    public async Task Handle_CompleteAsTechnicianWhenProfileMissing_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(TechnicianCommand());

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_CompleteAsTechnician_ActivatesProfileAndPublishesCompletion()
    {
        // Arrange
        GivenByUser(Incomplete());
        var sut = CreateSut();

        // Act
        var profile = await sut.Handle(TechnicianCommand());

        // Assert
        Assert.Equal((EProfileStatus.Active, EBusinessRole.Technician), (profile.Status, profile.BusinessRole!.Value));
        Assert.IsType<ProfileCompletedEvent>(_mediator.Calls.Single().Args[0]);
    }

    [Fact]
    public async Task Handle_CompleteAsTechnicianWithInvalidDni_ThrowsInvalidDniException()
    {
        // Arrange
        GivenByUser(Incomplete());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(TechnicianCommand(dni: "123"));

        // Assert
        await Assert.ThrowsAsync<InvalidDniException>(act);
    }

    [Fact]
    public async Task Handle_CompleteAsTechnicianWithDuplicateDni_PropagatesUniquenessFailure()
    {
        // Arrange
        GivenByUser(Incomplete());
        _uniqueness.Throws(nameof(IProfileUniquenessChecker.EnsureDniIsUniqueAsync), new InvalidOperationException("DNI already registered"));
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(TechnicianCommand());

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_CompleteAsHomeowner_CreatesHomeownerRole()
    {
        // Arrange
        GivenByUser(Incomplete());
        var sut = CreateSut();

        // Act
        var profile = await sut.Handle(HomeownerCommand());

        // Assert
        Assert.Equal((EBusinessRole.HomeOwner, EContactTime.Evening), (profile.BusinessRole!.Value, profile.Homeowner!.PreferredContactTime));
    }

    [Fact]
    public async Task Handle_CompleteAsCompanyWithoutRepresentative_LeavesPersonalDataEmpty()
    {
        // Arrange
        GivenByUser(Incomplete());
        var sut = CreateSut();

        // Act
        var profile = await sut.Handle(CompanyCommand());

        // Assert
        Assert.Equal((EBusinessRole.Company, "Acme SAC"), (profile.BusinessRole!.Value, profile.Company!.CompanyData.CompanyName));
        Assert.Null(profile.PersonalData);
    }

    [Fact]
    public async Task Handle_CompleteAsCompanyWithRepresentative_StoresPersonalData()
    {
        // Arrange
        GivenByUser(Incomplete());
        var sut = CreateSut();

        // Act
        var profile = await sut.Handle(CompanyCommand(firstName: "Luis"));

        // Assert
        Assert.Equal("Luis", profile.PersonalData!.FirstName);
    }

    [Fact]
    public async Task Handle_CompleteAlreadyActiveProfile_ThrowsInvalidProfileStatusException()
    {
        // Arrange
        GivenByUser(Homeowner());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(TechnicianCommand());

        // Assert
        await Assert.ThrowsAsync<InvalidProfileStatusException>(act);
    }

    [Fact]
    public async Task Handle_UpdatePersonalDataByAnotherUser_ThrowsUnauthorizedProfileAccessException()
    {
        // Arrange
        var profile = GivenById(Homeowner());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new UpdateProfilePersonalDataCommand(profile.ProfileId.Value, Ids.OtherUser, "Eva", null, null, null, null,
            null, null, null, null));

        // Assert
        await Assert.ThrowsAsync<UnauthorizedProfileAccessException>(act);
    }

    [Fact]
    public async Task Handle_UpdatePersonalDataWithPartialAddress_KeepsOtherAddressFields()
    {
        // Arrange
        var profile = GivenById(Homeowner());
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new UpdateProfilePersonalDataCommand(profile.ProfileId.Value, Ids.User, "Eva", null, null, null, null,
            null, "Cusco", null, null));

        // Assert
        Assert.Equal(("Eva", "Cusco", "Main"), (result.PersonalData!.FirstName, result.PersonalData.Address.City, result.PersonalData.Address.Street));
    }

    [Fact]
    public async Task Handle_UpdatePersonalDataWhenProfileMissing_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new UpdateProfilePersonalDataCommand(ProfileId.NewProfileId().Value, Ids.User, "Eva", null, null, null,
            null, null, null, null, null));

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_UpdateTechnicianData_UpdatesExperienceAndAboutMe()
    {
        // Arrange
        var profile = GivenById(Technician());
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new UpdateTechnicianDataCommand(profile.ProfileId.Value, Ids.User, null, 9, "Solar expert"));

        // Assert
        Assert.Equal((9, "Solar expert"), (result.Technician!.ExperienceYears, result.Technician.AboutMe));
    }

    [Fact]
    public async Task Handle_UpdateTechnicianDataOnHomeowner_ThrowsInvalidBusinessRoleException()
    {
        // Arrange
        var profile = GivenById(Homeowner());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new UpdateTechnicianDataCommand(profile.ProfileId.Value, Ids.User, null, 9, null));

        // Assert
        await Assert.ThrowsAsync<InvalidBusinessRoleException>(act);
    }

    [Fact]
    public async Task Handle_UpdateCommunicationPreferences_MergesWithCurrentChannels()
    {
        // Arrange
        var profile = GivenById(Homeowner());
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new UpdateCommunicationPreferencesCommand(profile.ProfileId.Value, Ids.User, null, true, null, null, null));

        // Assert
        var prefs = result.Homeowner!.CommunicationPreferences;
        Assert.Equal((true, true, false), (prefs.SmsNotifications, prefs.EmailNotifications, prefs.PushNotifications));
    }

    [Fact]
    public async Task Handle_UpdateCommunicationPreferencesDisablingAllChannels_ThrowsAtLeastOneNotificationChannelRequiredException()
    {
        // Arrange
        var profile = GivenById(Homeowner());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new UpdateCommunicationPreferencesCommand(profile.ProfileId.Value, Ids.User, false, false, false, null, null));

        // Assert
        await Assert.ThrowsAsync<AtLeastOneNotificationChannelRequiredException>(act);
    }

    [Fact]
    public async Task Handle_UploadProfilePicture_StoresUploadedPhoto()
    {
        // Arrange
        var profile = GivenById(Homeowner());
        _storage.Returns(nameof(IFileStorageService.UploadProfilePhotoAsync), new FileUploadResult("https://img/a.jpg", "prov-a", "jpg", 10));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new UploadProfilePictureCommand(profile.ProfileId.Value, Ids.User, new MemoryStream([1]), "a.jpg"));

        // Assert
        Assert.Equal(("https://img/a.jpg", "prov-a"), (result.Photo!.PublicUrl, result.Photo.ProviderId));
    }

    [Fact]
    public async Task Handle_GetPhotoUploadUrl_ReturnsSignedUploadForOwner()
    {
        // Arrange
        var profile = GivenById(Homeowner());
        var signed = new SignedUploadData("https://upload", "sig", 1, "key");
        _storage.Returns(nameof(IFileStorageService.GetSignedUploadUrlForProfileAsync), signed);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetProfilePhotoUploadUrlCommand(profile.ProfileId.Value, Ids.User));

        // Assert
        Assert.Same(signed, result);
    }

    [Fact]
    public async Task Handle_UpdatePhotoWhenPreviousPhotoExists_DeletesPreviousPhoto()
    {
        // Arrange
        var profile = GivenById(Homeowner());
        profile.UpdateProfilePhoto(ProfilePhoto.Create("https://img/old.jpg", "prov-old"));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new UpdateProfilePhotoCommand(profile.ProfileId.Value, Ids.User, "prov-new", "https://img/new.jpg", "jpg", 5));

        // Assert
        Assert.Equal("prov-new", result.Photo!.ProviderId);
        Assert.Equal("prov-old", _storage.CallsTo(nameof(IFileStorageService.DeleteProfilePhotoAsync)).Single().Arg<string>(1));
    }

    [Fact]
    public async Task Handle_UpdatePhotoWhenStorageFails_SchedulesOrphanCleanupAndRethrows()
    {
        // Arrange
        var profile = GivenById(Homeowner());
        profile.UpdateProfilePhoto(ProfilePhoto.Create("https://img/old.jpg", "prov-old"));
        _storage.Throws(nameof(IFileStorageService.DeleteProfilePhotoAsync), new HttpRequestException("cdn down"));
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new UpdateProfilePhotoCommand(profile.ProfileId.Value, Ids.User, "prov-new", "https://img/new.jpg", "jpg", 5));

        // Assert
        await Assert.ThrowsAsync<HttpRequestException>(act);
        Assert.Equal("prov-new", _orphans.Calls.Single().Arg<OrphanedFileDeletion>().ProviderId);
    }

    [Fact]
    public async Task Handle_RemovePhotoWhenDeletionFails_SchedulesOrphanCleanup()
    {
        // Arrange
        var profile = GivenById(Homeowner());
        profile.UpdateProfilePhoto(ProfilePhoto.Create("https://img/old.jpg", "prov-old"));
        _storage.Throws(nameof(IFileStorageService.DeleteProfilePhotoAsync), new HttpRequestException("cdn down"));
        var sut = CreateSut();

        // Act
        await sut.Handle(new RemoveProfilePhotoCommand(profile.ProfileId.Value, Ids.User));

        // Assert
        Assert.Null(profile.Photo);
        Assert.Equal("prov-old", _orphans.Calls.Single().Arg<OrphanedFileDeletion>().ProviderId);
    }

    [Fact]
    public async Task Handle_RemovePhotoWithoutPhoto_DoesNotCallStorage()
    {
        // Arrange
        var profile = GivenById(Homeowner());
        var sut = CreateSut();

        // Act
        await sut.Handle(new RemoveProfilePhotoCommand(profile.ProfileId.Value, Ids.User));

        // Assert
        Assert.Empty(_storage.Calls);
    }

    [Fact]
    public async Task Handle_DeactivateActiveProfile_MarksDeactivated()
    {
        // Arrange
        var profile = GivenById(Homeowner());
        var sut = CreateSut();

        // Act
        await sut.Handle(new DeactivateProfileCommand(profile.ProfileId.Value, Ids.User, "moving"));

        // Assert
        Assert.Equal(EProfileStatus.Deactivated, profile.Status);
    }

    [Fact]
    public async Task Handle_ReactivateDeactivatedProfile_MarksActive()
    {
        // Arrange
        var profile = GivenById(Homeowner());
        profile.Deactivate();
        var sut = CreateSut();

        // Act
        await sut.Handle(new ReactivateProfileCommand(profile.ProfileId.Value, Ids.User));

        // Assert
        Assert.Equal(EProfileStatus.Active, profile.Status);
    }

    [Fact]
    public async Task Handle_GrantIoTCertification_CertifiesTechnician()
    {
        // Arrange
        var profile = GivenByTechnician(Technician());
        var sut = CreateSut();

        // Act
        await sut.Handle(new GrantIoTCertificationCommand(profile.Technician!.TechnicianId.Value, "IoT Pro", "IEEE", DateTime.UtcNow.AddYears(-1),
            null, "C-1", null));

        // Assert
        Assert.Equal((true, "IoT Pro"), (profile.Technician.IsIoTCertified, profile.Technician.IoTCertification!.Name));
    }

    [Fact]
    public async Task Handle_GrantIoTCertificationForUnknownTechnician_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new GrantIoTCertificationCommand(Ids.Technician, "IoT Pro", "IEEE", DateTime.UtcNow, null, null, null));

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_RevokeIoTCertification_RemovesCertification()
    {
        // Arrange
        var profile = GivenByTechnician(Technician());
        var sut = CreateSut();
        await sut.Handle(new GrantIoTCertificationCommand(profile.Technician!.TechnicianId.Value, "IoT Pro", "IEEE", DateTime.UtcNow.AddYears(-1),
            null, null, null));

        // Act
        await sut.Handle(new RevokeIoTCertificationCommand(profile.Technician.TechnicianId.Value));

        // Assert
        Assert.False(profile.Technician.IsIoTCertified);
    }
}
