using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Entities;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Services;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Electrolink.Backend.UnitTests.Planning;

internal static class PlanningTestData
{
    public const string Homeowner = "ho-1";
    public const string Company = "comp-1";
    public static readonly ClientIdentity Client = ClientIdentity.FromHomeowner(Homeowner);
    public static readonly ClientIdentity CompanyClient = ClientIdentity.FromCompany(Company);
    public static readonly TechnicianId Technician = TechnicianId.From(Ids.Technician);

    public static Geolocation Location => Geolocation.Create(-12.1, -77.03, null, "MANUAL");

    public static ServiceRequest Draft(ClientIdentity? client = null)
    {
        var request = ServiceRequest.Initiate(client ?? Client, false, 3);
        request.ClearDomainEvents();
        return request;
    }

    public static ServiceRequest WithProperty(ClientIdentity? client = null)
    {
        var request = Draft(client);
        request.SelectProperty(PropertyId.NewPropertyId(), Location);
        request.ClearDomainEvents();
        return request;
    }

    public static ServiceRequest WithCategory()
    {
        var request = WithProperty();
        request.SelectCategory(EServiceCategory.Repair);
        return request;
    }

    public static ServiceRequest ReadyToConfirm(bool priority = false)
    {
        var request = WithCategory();
        request.AddDetails(RequestPreferences.Create("Tripping breaker", 120, Money.Of(80, ECurrency.Pen), "2025-05", "R-1",
            [DateOnly.FromDateTime(DateTime.Today.AddDays(5))], ETimePreference.Morning), priority);
        request.ClearDomainEvents();
        return request;
    }

    public static ServiceRequest PendingAssignment(bool priority = false)
    {
        var request = ReadyToConfirm(priority);
        request.Confirm();
        request.ClearDomainEvents();
        return request;
    }

    public static ServiceRequest Assigned()
    {
        var request = PendingAssignment();
        request.MarkAsAssigned(AssignmentId.NewAssignmentId(), Technician, RecipeSnapshot.FromRecipe(Recipe()));
        return request;
    }

    public static ServiceRequest InstallationRequest(string userId = Ids.User, ClientIdentity? client = null) =>
        ServiceRequest.CreateIoTInstallationRequest("sub-1", userId, client ?? CompanyClient, 2, DateTime.UtcNow.AddDays(10));

    public static IComponentTypeValidator AcceptingValidator() => new Fake<IComponentTypeValidator>().Object;

    public static ServiceCatalog CatalogWithRecipe(string name = "Breaker repair", TechnicianId? technician = null,
        EServiceCategory category = EServiceCategory.Repair, decimal price = 150)
    {
        var catalog = ServiceCatalog.Create(technician ?? Technician);
        catalog.AddRecipe(name, "Replace faulty breaker", category, false,
            [ComponentRequirementItem.Create("ctype-1", "Breaker", 1, true)], EstimatedDuration.FromMinutes(90),
            ServicePricing.Create(Money.Of(50, ECurrency.Pen), Money.Of(100, ECurrency.Pen), Money.Of(price, ECurrency.Pen)), [], [],
            WarrantyPeriod.OfMonths(6), AcceptingValidator());
        catalog.ClearDomainEvents();
        return catalog;
    }

    public static ServiceRecipe Recipe(TechnicianId? technician = null) => CatalogWithRecipe(technician: technician).Recipes.Single();

    public static (string ProfileId, string ProfileStatus, string? BusinessRole, string? RoleSubjectId, string? SubscriptionTier) Claims(
        string role, string subject, string status = "Active") => ("prof-1", status, role, subject, null);

    /// <summary>Profiles facade whose actor is the active, concordant Company <see cref="Company"/>.</summary>
    public static Fake<IProfilesContextFacade> CompanyProfiles() => new Fake<IProfilesContextFacade>()
        .Returns(nameof(IProfilesContextFacade.GetProfileClaimsAsync), Claims("Company", Company))
        .Returns(nameof(IProfilesContextFacade.GetProfileIdByCompanyIdAsync), "prof-1")
        .Returns(nameof(IProfilesContextFacade.IsCompanyActiveAsync), true);

    public static EnrichedCandidate Candidate(string technician, double distanceKm = 5, double rating = 4, bool hasComponents = true,
        int responseMinutes = 30, int experience = 5) =>
        new(technician, rating, Recipe(TechnicianId.From(technician)), distanceKm, experience, [], false, hasComponents, responseMinutes);
}
