using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Electrolink.Backend.UnitTests.Assets;

internal static class AssetsTestData
{
    public const string Homeowner = "ho-1";
    public const string Company = "comp-1";
    public const string InstallationRequest = "req-1";

    public static ClientIdentity HomeownerOwner => ClientIdentity.FromHomeowner(Homeowner);

    public static Address Address(string street = "Main") => Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects.Address.Create(
        street, "100", "Miraflores", "Lima", "PE", "15074");

    public static Geolocation Location => Geolocation.Create(-12.1, -77.03, 10, "gps");

    public static Property NewProperty(ClientIdentity? owner = null) => Property.Create(owner ?? HomeownerOwner, Address(), Location);

    public static IoTDevice InStockDevice(string? technician = null) => technician == null
        ? IoTDevice.Register(SerialNumber.From("SN-" + Guid.NewGuid().ToString("N")[..8]), ApiKeyHash.From("hash"), "1.0")
        : IoTDevice.RegisterForTechnician(SerialNumber.From("SN-" + Guid.NewGuid().ToString("N")[..8]), ApiKeyHash.From("hash"), "1.0",
            TechnicianId.From(technician));

    public static IoTDevice AssignedDevice(Property property, string? technician = null)
    {
        var device = InStockDevice(technician);
        device.AssignToProperty(property.Id, InstallationRequestId.From(InstallationRequest));
        device.ClearDomainEvents();
        return device;
    }

    public static IoTDevice InstalledDevice(Property property)
    {
        var device = AssignedDevice(property);
        device.RecordInstallation(TechnicianId.From(Ids.Technician), property.Id, "1.1", DateTime.UtcNow);
        device.ClearDomainEvents();
        return device;
    }

    public static (string ProfileId, string ProfileStatus, string? BusinessRole, string? RoleSubjectId, string? SubscriptionTier) Claims(
        string role, string subject, string status = "Active") => ("prof-1", status, role, subject, null);

    /// <summary>Profiles facade where the actor is an active technician whose technician id matches the profile.</summary>
    public static Fake<IProfilesContextFacade> TechnicianProfiles(string technician = Ids.Technician) => new Fake<IProfilesContextFacade>()
        .Returns(nameof(IProfilesContextFacade.GetProfileClaimsAsync), Claims("Technician", technician))
        .Returns(nameof(IProfilesContextFacade.GetTechnicianIdByUserIdAsync), technician);

    /// <summary>Profiles facade where the actor is the active homeowner <see cref="Homeowner"/>.</summary>
    public static Fake<IProfilesContextFacade> HomeownerProfiles() => new Fake<IProfilesContextFacade>()
        .Returns(nameof(IProfilesContextFacade.GetProfileClaimsAsync), Claims("HomeOwner", Homeowner))
        .Returns(nameof(IProfilesContextFacade.IsHomeownerActiveAsync), true);
}
