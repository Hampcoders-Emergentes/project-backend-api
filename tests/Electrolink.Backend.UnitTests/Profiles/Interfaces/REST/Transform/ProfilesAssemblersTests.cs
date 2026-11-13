using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.ReadModels;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Profiles.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Profiles.Interfaces.REST.Transform;
using static Electrolink.Backend.UnitTests.Profiles.ProfilesTestData;

namespace Electrolink.Backend.UnitTests.Profiles.Interfaces.REST.Transform;

public class ProfilesCommandAssemblersTests
{
    private static readonly string Profile = Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects.ProfileId.NewProfileId().Value;

    [Fact]
    public void CompleteProfileAsCompany_ToCommandFromResource_MapsCompanyAndRepresentative()
    {
        // Arrange
        var resource = new CompleteProfileAsCompanyResource("Acme", "20123456789", "Av", "1", "Ate", "Lima", "PE", "15012", "Retail",
            ECompanySize.Large, "https://acme.pe", "Luis", "Diaz", Phone, DniNumber, BirthDate, "St", "2", "Mira", "Lima", "PE", "15074");

        // Act
        var command = CompleteProfileAsCompanyCommandFromResourceAssembler.ToCommandFromResource(resource, Ids.User);

        // Assert
        Assert.Equal((Ids.User, "Acme", ECompanySize.Large, "Luis", "15074"), (command.UserId, command.CompanyName, command.CompanySize,
            command.FirstName, command.PostalCode));
    }

    [Fact]
    public void CompleteProfileAsHomeowner_ToCommandFromResource_BuildsPreferencesAndEmergencyContact()
    {
        // Arrange
        var resource = new CompleteProfileAsHomeownerResource("Ana", "Diaz", Phone, DniNumber, BirthDate, "Main", "100", "Mira", "Lima", "PE", "15074",
            EContactTime.Afternoon, true, false, true, new EmergencyContactResource("Luis", "Brother", "+51911111111"));

        // Act
        var command = CompleteProfileAsHomeownerCommandFromResourceAssembler.ToCommandFromResource(resource, Ids.User);

        // Assert
        Assert.Equal((true, true, "Luis"), (command.CommunicationPreferences.SmsNotifications, command.CommunicationPreferences.PushNotifications,
            command.EmergencyContact!.Name));
    }

    [Fact]
    public void CompleteProfileAsHomeowner_ToCommandFromResourceWithoutChannels_ThrowsAtLeastOneNotificationChannelRequiredException()
    {
        // Arrange
        var resource = new CompleteProfileAsHomeownerResource("Ana", "Diaz", Phone, DniNumber, BirthDate, "Main", "100", "Mira", "Lima", "PE", "15074",
            EContactTime.Morning, false, false, false);

        // Act
        var act = () => CompleteProfileAsHomeownerCommandFromResourceAssembler.ToCommandFromResource(resource, Ids.User);

        // Assert
        Assert.Throws<Hampcoders.Electrolink.API.Profiles.Domain.Model.Exceptions.AtLeastOneNotificationChannelRequiredException>(act);
    }

    [Fact]
    public void CompleteProfileAsHomeowner_ToCommandFromResourceWithoutEmergencyContact_LeavesContactNull()
    {
        // Arrange
        var resource = new CompleteProfileAsHomeownerResource("Ana", "Diaz", Phone, DniNumber, BirthDate, "Main", "100", "Mira", "Lima", "PE", "15074",
            EContactTime.Morning, true, false, false);

        // Act
        var command = CompleteProfileAsHomeownerCommandFromResourceAssembler.ToCommandFromResource(resource, Ids.User);

        // Assert
        Assert.Null(command.EmergencyContact);
    }

    [Fact]
    public void CompleteProfileAsTechnician_ToCommandFromResource_MapsCoverageAndSpecialties()
    {
        // Arrange
        var resource = new CompleteProfileAsTechnicianResource("Ana", "Diaz", Phone, DniNumber, BirthDate, "Main", "100", "Mira", "Lima", "PE", "15074",
            [ESpecialty.Repair], 7, "Expert", -12.1, -77.0, 20);

        // Act
        var command = CompleteProfileAsTechnicianCommandFromResourceAssembler.ToCommandFromResource(resource, Ids.User);

        // Assert
        Assert.Equal((7, 20d, ESpecialty.Repair), (command.ExperienceYears, command.RadiusKm, command.Specialties.Single()));
    }

    [Fact]
    public void CreateStaffMember_ToCommandFromResource_MapsAllFields()
    {
        // Act
        var command = CreateStaffMemberCommandFromResourceAssembler.ToCommandFromResource(new CreateStaffMemberResource(Ids.User, "Ana", "Diaz", null));

        // Assert
        Assert.Equal(new CreateStaffMemberCommand(Ids.User, "Ana", "Diaz", null), command);
    }

    [Fact]
    public void UpdateHomeownerPreferences_ToCommandFromResource_MapsOptionalChannels()
    {
        // Arrange
        var resource = new UpdateHomeownerPreferencesResource(null, true, false, EContactTime.Evening, null);

        // Act
        var command = UpdateHomeownerPreferencesCommandFromResourceAssembler.ToCommandFromResource(resource, Profile, Ids.User);

        // Assert
        Assert.Equal(new UpdateCommunicationPreferencesCommand(Profile, Ids.User, null, true, false, EContactTime.Evening, null), command);
    }

    [Fact]
    public void UpdateHomeownerPreferences_ToCommandFromResourceWithContact_CreatesEmergencyContact()
    {
        // Arrange
        var resource = new UpdateHomeownerPreferencesResource(null, null, null, null, new EmergencyContactResource("Eva", "Sister", "+51922222222"));

        // Act
        var command = UpdateHomeownerPreferencesCommandFromResourceAssembler.ToCommandFromResource(resource, Profile, Ids.User);

        // Assert
        Assert.Equal(("Eva", "Sister"), (command.EmergencyContact!.Name, command.EmergencyContact.Relationship));
    }

    [Fact]
    public void UpdatePersonalData_ToCommandFromResource_MapsAllFields()
    {
        // Arrange
        var resource = new UpdatePersonalDataResource("Eva", null, Phone, "St", "2", null, "Cusco", "PE", null);

        // Act
        var command = UpdatePersonalDataCommandFromResourceAssembler.ToCommandFromResource(resource, Profile, Ids.User);

        // Assert
        Assert.Equal(new UpdateProfilePersonalDataCommand(Profile, Ids.User, "Eva", null, Phone, "St", "2", null, "Cusco", "PE", null), command);
    }

    [Fact]
    public void UpdateProfilePhoto_ToCommandFromResource_MapsPhotoData()
    {
        // Act
        var command = UpdateProfilePhotoCommandFromResourceAssembler.ToCommandFromResource(Profile, Ids.User,
            new UpdateProfilePhotoResource("prov-1", "https://img/1.jpg", "png", 99));

        // Assert
        Assert.Equal(new UpdateProfilePhotoCommand(Profile, Ids.User, "prov-1", "https://img/1.jpg", "png", 99), command);
    }

    [Fact]
    public void UpdateTechnicianData_ToCommandFromResource_MapsOptionalFields()
    {
        // Arrange
        var specialties = new List<ESpecialty> { ESpecialty.SolarInstallation };

        // Act
        var command = UpdateTechnicianDataCommandFromResourceAssembler.ToCommandFromResource(new UpdateTechnicianDataRequest(specialties, null, "x"),
            Profile, Ids.User);

        // Assert
        Assert.Equal((specialties, (int?)null, "x"), ((List<ESpecialty>)command.Specialties!, command.ExperienceYears, command.AboutMe));
    }
}

public class ProfilesResourceAssemblersTests
{
    [Fact]
    public void MyProfile_ToResourceFromIncompleteProfile_LeavesRoleSectionsNull()
    {
        // Arrange
        var profile = Incomplete();

        // Act
        var resource = MyProfileResourceFromEntityAssembler.ToResourceFromEntity(profile);

        // Assert
        Assert.Equal(("Incomplete", (string?)null, (string?)null), (resource.Status, resource.BusinessRole, resource.FirstName));
        Assert.Null(resource.Technician);
    }

    [Fact]
    public void MyProfile_ToResourceFromTechnician_MapsPersonalDataAndCoverage()
    {
        // Arrange
        var profile = Technician();

        // Act
        var resource = MyProfileResourceFromEntityAssembler.ToResourceFromEntity(profile);

        // Assert
        Assert.Equal(("Ana", DniNumber, BirthDate, "Technician"), (resource.FirstName, resource.Dni, resource.DateOfBirth, resource.BusinessRole));
        Assert.Equal(["Repair"], resource.Technician!.Specialties);
        Assert.Equal(15d, resource.Technician.RadiusKm);
    }

    [Fact]
    public void MyProfile_ToResourceFromHomeowner_MapsPreferences()
    {
        // Arrange
        var profile = Homeowner();

        // Act
        var resource = MyProfileResourceFromEntityAssembler.ToResourceFromEntity(profile);

        // Assert
        Assert.Equal(("Morning", true, (string?)null), (resource.Homeowner!.PreferredContactTime, resource.Homeowner.SmsNotifications,
            resource.Homeowner.EmergencyContactName));
    }

    [Fact]
    public void MyProfile_ToResourceFromCompany_MapsCompanyData()
    {
        // Arrange
        var profile = Company();

        // Act
        var resource = MyProfileResourceFromEntityAssembler.ToResourceFromEntity(profile);

        // Assert
        Assert.Equal(("Acme SAC", "20123456789", "Medium", "Ate"), (resource.Company!.CompanyName, resource.Company.TaxId,
            resource.Company.CompanySize, resource.Company.BillingDistrict));
        Assert.Null(resource.FirstName);
    }

    [Fact]
    public void MyProfile_ToResourceWithPhoto_MapsPictureUrl()
    {
        // Arrange
        var profile = Homeowner();
        profile.UpdateProfilePhoto(ProfilePhoto.Create("https://img/me.jpg", "prov-me"));

        // Act
        var resource = MyProfileResourceFromEntityAssembler.ToResourceFromEntity(profile);

        // Assert
        Assert.Equal("https://img/me.jpg", resource.ProfilePictureUrl);
    }

    [Fact]
    public void ProfileStatus_ToResourceFromReadModel_MapsAllFields()
    {
        // Act
        var resource = ProfileStatusResourceFromReadModelAssembler.ToResourceFromReadModel(new ProfileStatusReadModel("prof-1", Ids.User, "Active", 100));

        // Assert
        Assert.Equal(new ProfileStatusResource("prof-1", Ids.User, "Active", 100), resource);
    }

    [Fact]
    public void StaffMember_ToResourceFromEntityWithoutZone_LeavesZoneNull()
    {
        // Arrange
        var member = StaffMember.Create(Ids.User, "Ana", "Diaz", "+51999");

        // Act
        var resource = StaffMemberResourceFromEntityAssembler.ToResourceFromEntity(member);

        // Assert
        Assert.Equal((member.Id.Value, "Ana", (string?)null, true), (resource.Id, resource.FirstName, resource.AssignedZone, resource.IsActive));
    }

    [Fact]
    public void StaffMember_ToResourceFromEntityWithZone_MapsRegion()
    {
        // Arrange
        var member = StaffMember.Create(Ids.User, "Ana", "Diaz", null);
        member.AssignZone(StaffZone.Create("Lima Norte", -11.9, -77.0, 10));

        // Act
        var resource = StaffMemberResourceFromEntityAssembler.ToResourceFromEntity(member);

        // Assert
        Assert.Equal("Lima Norte", resource.AssignedZone);
    }
}
