using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Profiles.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Electrolink.Backend.UnitTests.Profiles;

internal static class ProfilesTestData
{
    public const string Phone = "+51987654321";
    public const string DniNumber = "12345678";
    public const string BirthDate = "1990-05-01";

    public static IProfileUniquenessChecker Unique() => new Fake<IProfileUniquenessChecker>().Object;

    public static Profile Incomplete(string userId = Ids.User)
    {
        var profile = Profile.Create(UserId.From(userId));
        profile.ClearDomainEvents();
        return profile;
    }

    private static PersonalData Personal() => PersonalData.Create("Ana", "Diaz", PhoneNumber.From(Phone),
        Hampcoders.Electrolink.API.Profiles.Domain.Model.ValueObjects.Dni.From(DniNumber), DateOfBirth.From(BirthDate),
        Address.Create("Main", "100", "Miraflores", "Lima", "PE", "15074"));

    public static Profile Technician(string userId = Ids.User)
    {
        var profile = Incomplete(userId);
        profile.CompleteAsTechnician(Personal(), TechnicianData.Create([ESpecialty.Repair], 5, "Licensed electrician",
            ServiceArea.FromPointAndRadius(-12.1, -77.0, 15)), Unique()).GetAwaiter().GetResult();
        profile.ClearDomainEvents();
        return profile;
    }

    public static Profile Homeowner(string userId = Ids.User)
    {
        var profile = Incomplete(userId);
        profile.CompleteAsHomeowner(Personal(), HomeownerData.Create(EContactTime.Morning, CommunicationPreferences.Create(true, false, false)),
            Unique()).GetAwaiter().GetResult();
        profile.ClearDomainEvents();
        return profile;
    }

    public static Profile Company(string userId = Ids.User)
    {
        var profile = Incomplete(userId);
        profile.CompleteAsCompany(CompanyData.Create("Acme SAC", TaxId.From("20123456789"),
            Address.Create("Av. Industrial", "500", "Ate", "Lima", "PE", "15012"), "Retail", ECompanySize.Medium, "https://acme.pe"),
            Unique()).GetAwaiter().GetResult();
        profile.ClearDomainEvents();
        return profile;
    }

    public static CompleteProfileAsTechnicianCommand TechnicianCommand(string userId = Ids.User, string dni = DniNumber) => new(userId, "Ana", "Diaz",
        Phone, dni, BirthDate, "Main", "100", "Miraflores", "Lima", "PE", "15074", [ESpecialty.Repair, ESpecialty.SolarInstallation], 5,
        "Licensed", -12.1, -77.0, 15);

    public static CompleteProfileAsHomeownerCommand HomeownerCommand(string userId = Ids.User) => new(userId, "Ana", "Diaz", Phone, DniNumber,
        BirthDate, "Main", "100", "Miraflores", "Lima", "PE", "15074", EContactTime.Evening, CommunicationPreferences.Create(false, true, false));

    public static CompleteProfileAsCompanyCommand CompanyCommand(string userId = Ids.User, string? firstName = null) => new(userId, "Acme SAC",
        "20123456789", "Av. Industrial", "500", "Ate", "Lima", "PE", "15012", "Retail", ECompanySize.Large, null, firstName,
        firstName is null ? null : "Diaz", firstName is null ? null : Phone, firstName is null ? null : DniNumber,
        firstName is null ? null : BirthDate, null, null, null, null, null, null);
}
