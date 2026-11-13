using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Assets.Interfaces.ACL;
using Hampcoders.Electrolink.API.Monitoring.Interfaces.ACL;
using Hampcoders.Electrolink.API.Planning.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Planning.Domain.Services;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.ACL;
using Microsoft.Extensions.Logging.Abstractions;
using static Electrolink.Backend.UnitTests.Planning.PlanningTestData;
using IoTContextSnapshot = Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects.IoTContextSnapshot;

namespace Electrolink.Backend.UnitTests.Planning.Application.Internal.CommandServices;

public class ServiceRequestCommandServiceTests
{
    private static readonly ClientIdentity Stranger = ClientIdentity.FromHomeowner("ho-2");
    private readonly Fake<IServiceRequestRepository> _requests = new();
    private readonly Fake<IServiceAssignmentCommandService> _assignments = new();
    private readonly Fake<ISubscriptionContextFacade> _subscriptions = new();
    private readonly Fake<IProfilesContextFacade> _profiles = new();
    private readonly Fake<IAssetsContextFacade> _assets = new();
    private readonly Fake<IContextSnapshotService> _snapshots = new();

    public ServiceRequestCommandServiceTests()
    {
        _profiles.Returns(nameof(IProfilesContextFacade.IsHomeownerActiveAsync), true);
        _subscriptions.Returns(nameof(ISubscriptionContextFacade.GetRequestEligibilityAsync), (true, "PRO", (int?)3, true));
    }

    private ServiceRequestCommandService CreateSut() => new(_requests.Object, new Fake<IServiceCatalogRepository>().Object, _assignments.Object,
        _subscriptions.Object, new ExternalProfilesService(_profiles.Object), _assets.Object, _snapshots.Object, new Fake<IUnitOfWork>().Object,
        NullLogger<ServiceRequestCommandService>.Instance);

    private ServiceRequest Given(ServiceRequest request)
    {
        _requests.Returns(nameof(IServiceRequestRepository.FindByIdAsync), request);
        return request;
    }

    private static AddServiceDetailsCommand Details(ServiceRequest r, string currency = "pen") => new(r.RequestId, Client, "Tripping", 120, 80,
        currency, "2025-05", "R-1", true, [DateTime.Today.AddDays(5).ToString("yyyy-MM-dd")], "afternoon");

    [Fact]
    public async Task Handle_InitiateForInactiveClient_ThrowsInactiveHomeownerException()
    {
        // Arrange
        _profiles.Returns(nameof(IProfilesContextFacade.IsHomeownerActiveAsync), false);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new InitiateServiceRequestCommand(Client));

        // Assert
        await Assert.ThrowsAsync<InactiveHomeownerException>(act);
    }

    [Fact]
    public async Task Handle_InitiateWhenMonthlyLimitReached_ThrowsRequestLimitReachedException()
    {
        // Arrange
        _subscriptions.Returns(nameof(ISubscriptionContextFacade.GetRequestEligibilityAsync), (false, "FREE", (int?)0, false));
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new InitiateServiceRequestCommand(Client));

        // Assert
        await Assert.ThrowsAsync<RequestLimitReachedException>(act);
    }

    [Fact]
    public async Task Handle_InitiateForEligibleClient_PersistsDraftRequest()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var requestId = await sut.Handle(new InitiateServiceRequestCommand(Client));

        // Assert
        var request = _requests.Calls.Single().Arg<ServiceRequest>();
        Assert.Equal((requestId, ERequestStatus.Draft), (request.RequestId, request.Status));
    }

    [Fact]
    public async Task Handle_SelectPropertyByAnotherClient_ThrowsUnauthorizedRequestAccessException()
    {
        // Arrange
        var request = Given(Draft());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new SelectPropertyForRequestCommand(request.RequestId, Stranger, PropertyId.NewPropertyId()));

        // Assert
        await Assert.ThrowsAsync<UnauthorizedRequestAccessException>(act);
    }

    [Fact]
    public async Task Handle_SelectPropertyNotOwned_ThrowsPropertyUnavailableForRequestException()
    {
        // Arrange
        var request = Given(Draft());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new SelectPropertyForRequestCommand(request.RequestId, Client, PropertyId.NewPropertyId()));

        // Assert
        await Assert.ThrowsAsync<PropertyUnavailableForRequestException>(act);
    }

    [Fact]
    public async Task Handle_SelectOwnedProperty_StoresPropertyAndGeolocation()
    {
        // Arrange
        var request = Given(Draft());
        var property = PropertyId.NewPropertyId();
        _assets.Returns(nameof(IAssetsContextFacade.GetPropertyGeolocationAsync), (-12.5, -77.5));
        var sut = CreateSut();

        // Act
        await sut.Handle(new SelectPropertyForRequestCommand(request.RequestId, Client, property));

        // Assert
        Assert.Equal((ERequestStatus.PropertySelected, property, -12.5), (request.Status, request.PropertyId, request.Geolocation!.Latitude));
    }

    [Fact]
    public async Task Handle_SelectCategory_StoresRequestedCategory()
    {
        // Arrange
        var request = Given(WithProperty());
        var sut = CreateSut();

        // Act
        await sut.Handle(new SelectServiceRecipeCommand(request.RequestId, Client, EServiceCategory.Inspection));

        // Assert
        Assert.Equal((ERequestStatus.CategorySelected, EServiceCategory.Inspection), (request.Status, request.RequestedCategory!.Value));
    }

    [Fact]
    public async Task Handle_SelectCategoryWhenMissing_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new SelectServiceRecipeCommand(RequestId.NewId(), Client, EServiceCategory.Inspection));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_AddDetailsWithUnknownCurrency_ThrowsArgumentException()
    {
        // Arrange
        var request = Given(WithCategory());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Details(request, "btc"));

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_AddDetailsWithValidData_MarksRequestReadyToConfirm()
    {
        // Arrange
        var request = Given(WithCategory());
        var sut = CreateSut();

        // Act
        await sut.Handle(Details(request));

        // Assert
        Assert.Equal((ERequestStatus.ReadyToConfirm, true, ETimePreference.Afternoon),
            (request.Status, request.IsPriority, request.Preferences!.TimePreference));
    }

    [Fact]
    public async Task Handle_ConfirmWhenLimitReached_ThrowsRequestLimitReachedException()
    {
        // Arrange
        var request = Given(ReadyToConfirm());
        _subscriptions.Returns(nameof(ISubscriptionContextFacade.GetRequestEligibilityAsync), (false, "FREE", (int?)0, false));
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new ConfirmServiceRequestCommand(request.RequestId, Client));

        // Assert
        await Assert.ThrowsAsync<RequestLimitReachedException>(act);
    }

    [Fact]
    public async Task Handle_ConfirmReadyRequest_StoresIotSnapshotAndRunsMatching()
    {
        // Arrange
        var request = Given(ReadyToConfirm());
        var snapshot = new IoTContextSnapshot("dev-1", [], [], DateTime.UtcNow);
        _snapshots.Returns(nameof(IContextSnapshotService.CaptureAsync), snapshot);
        var sut = CreateSut();

        // Act
        await sut.Handle(new ConfirmServiceRequestCommand(request.RequestId, Client));

        // Assert
        Assert.Equal((ERequestStatus.PendingAssignment, snapshot), (request.Status, request.IotContextSnapshot));
        Assert.Equal(request.RequestId, _assignments.Calls.Single().Arg<ExecuteMatchingAlgorithmCommand>().RequestId);
    }

    [Fact]
    public async Task Handle_CancelPendingRequest_MarksRequestCancelled()
    {
        // Arrange
        var request = Given(PendingAssignment());
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new CancelServiceRequestCommand(request.RequestId, Client, "changedmind"));

        // Assert
        Assert.True(result);
        Assert.Equal(ERequestStatus.Cancelled, request.Status);
    }

    [Fact]
    public async Task Handle_CancelAssignedRequest_ThrowsCannotCancelAssignedRequestException()
    {
        // Arrange
        var request = Given(Assigned());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new CancelServiceRequestCommand(request.RequestId, Client, "ChangedMind"));

        // Assert
        await Assert.ThrowsAsync<CannotCancelAssignedRequestException>(act);
    }

    [Fact]
    public async Task Handle_CancelWithUnknownReason_ThrowsInvalidCancellationReasonException()
    {
        // Arrange
        var request = Given(PendingAssignment());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new CancelServiceRequestCommand(request.RequestId, Client, "boredom"));

        // Assert
        await Assert.ThrowsAsync<InvalidCancellationReasonException>(act);
    }

    [Fact]
    public async Task Handle_MarkAsAssignedWhenMissing_ThrowsException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new MarkServiceRequestAsAssignedCommand(RequestId.NewId(), AssignmentId.NewAssignmentId(), Technician,
            RecipeSnapshot.FromRecipe(Recipe())));

        // Assert
        await Assert.ThrowsAsync<Exception>(act);
    }

    [Fact]
    public async Task Handle_MarkAsAssignedPendingRequest_StoresTechnician()
    {
        // Arrange
        var request = Given(PendingAssignment());
        var sut = CreateSut();

        // Act
        await sut.Handle(new MarkServiceRequestAsAssignedCommand(request.RequestId, AssignmentId.NewAssignmentId(), Technician,
            RecipeSnapshot.FromRecipe(Recipe())));

        // Assert
        Assert.Equal((ERequestStatus.Assigned, Technician), (request.Status, request.SelectedTechnicianId));
    }

    [Fact]
    public async Task Handle_ReactivateAssignedRequest_ReturnsRequestToQueue()
    {
        // Arrange
        var request = Given(Assigned());
        var sut = CreateSut();

        // Act
        await sut.Handle(new ReactivateServiceRequestCommand(request.RequestId, Client, "no-show"));

        // Assert
        Assert.Equal((ERequestStatus.PendingAssignment, (TechnicianId?)null), (request.Status, request.SelectedTechnicianId));
    }

    [Fact]
    public async Task Handle_ReactivateForAnotherClient_LeavesRequestUnchanged()
    {
        // Arrange
        var request = Given(Assigned());
        var sut = CreateSut();

        // Act
        await sut.Handle(new ReactivateServiceRequestCommand(request.RequestId, Stranger, "no-show"));

        // Assert
        Assert.Equal(ERequestStatus.Assigned, request.Status);
    }

    [Fact]
    public async Task Handle_ReactivateRequestNotAssigned_LeavesRequestUnchanged()
    {
        // Arrange
        var request = Given(PendingAssignment());
        var sut = CreateSut();

        // Act
        await sut.Handle(new ReactivateServiceRequestCommand(request.RequestId, Client, "no-show"));

        // Assert
        Assert.Equal(0, _requests.CallCount(nameof(IServiceRequestRepository.Update)));
    }

    [Fact]
    public async Task Handle_ReactivateMissingRequest_CompletesWithoutChanges()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(new ReactivateServiceRequestCommand(RequestId.NewId(), Client, "no-show"));

        // Assert
        Assert.Equal(0, _requests.CallCount(nameof(IServiceRequestRepository.Update)));
    }
}

public class ServiceCatalogCommandServiceTests
{
    private readonly Fake<IServiceCatalogRepository> _catalogs = new();
    private readonly Fake<IMonitoringContextFacade> _monitoring = new();
    private readonly Fake<IAssetsContextFacade> _assets = new();

    public ServiceCatalogCommandServiceTests() =>
        _assets.Returns(nameof(IAssetsContextFacade.GetComponentTypeNameAsync), "Breaker");

    private ServiceCatalogCommandService CreateSut() => new(_catalogs.Object, new ExternalMonitoringService(_monitoring.Object),
        new ExternalAssetsService(_assets.Object), AcceptingValidator(), new Fake<IUnitOfWork>().Object,
        NullLogger<ServiceCatalogCommandService>.Instance);

    private ServiceCatalog GivenCatalog(ServiceCatalog catalog)
    {
        _catalogs.Returns(nameof(IServiceCatalogRepository.FindByTechnicianIdAsync), catalog);
        return catalog;
    }

    private static CreateServiceRecipeCommand RecipeCommand(CatalogId catalogId, string currency = "PEN") => new(catalogId, Technician,
        "Inspection", "Full inspection", "inspection", false, [ComponentRequirementItem.Create("ctype-1", "", 1, true)], 1, 30, 20, 80, 100,
        currency, [], [], 3);

    [Fact]
    public async Task Handle_CreateCatalogWhenExists_ThrowsCatalogAlreadyExistsException()
    {
        // Arrange
        _catalogs.Returns(nameof(IServiceCatalogRepository.ExistsByTechnicianIdAsync), true);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new CreateServiceCatalogCommand(Technician, ProfileId.From("prof-1")));

        // Assert
        await Assert.ThrowsAsync<CatalogAlreadyExistsException>(act);
    }

    [Fact]
    public async Task Handle_CreateCatalogForNewTechnician_PersistsEmptyCatalog()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var catalog = await sut.Handle(new CreateServiceCatalogCommand(Technician, ProfileId.From("prof-1")));

        // Assert
        Assert.Equal((Technician, ECatalogStatus.Empty), (catalog!.TechnicianId, catalog.Status));
    }

    [Fact]
    public async Task Handle_CreateRecipeWhenCatalogMissing_ThrowsCatalogNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(RecipeCommand(CatalogId.NewId()));

        // Assert
        await Assert.ThrowsAsync<CatalogNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_CreateRecipeInForeignCatalog_ThrowsUnauthorizedCatalogAccessException()
    {
        // Arrange
        GivenCatalog(ServiceCatalog.Create(Technician));
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(RecipeCommand(CatalogId.NewId()));

        // Assert
        await Assert.ThrowsAsync<UnauthorizedCatalogAccessException>(act);
    }

    [Fact]
    public async Task Handle_CreateRecipeWithUnknownComponentType_ThrowsComponentTypeNotFoundException()
    {
        // Arrange
        var catalog = GivenCatalog(ServiceCatalog.Create(Technician));
        _assets.Returns(nameof(IAssetsContextFacade.GetComponentTypeNameAsync), null);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(RecipeCommand(catalog.CatalogId));

        // Assert
        await Assert.ThrowsAsync<ComponentTypeNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_CreateRecipeWithValidData_AddsRecipeWithResolvedComponentNames()
    {
        // Arrange
        var catalog = GivenCatalog(ServiceCatalog.Create(Technician));
        var sut = CreateSut();

        // Act
        var recipe = await sut.Handle(RecipeCommand(catalog.CatalogId));

        // Assert
        Assert.Equal((EServiceCategory.Inspection, "Breaker", ECatalogStatus.Active),
            (recipe!.ServiceCategory, recipe.ComponentRequirements.Single().ComponentTypeName, catalog.Status));
    }

    [Fact]
    public async Task Handle_CreateRecipeWithPriceBelowCosts_ThrowsInvalidOperationException()
    {
        // Arrange
        var catalog = GivenCatalog(ServiceCatalog.Create(Technician));
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(RecipeCommand(catalog.CatalogId) with { TotalPrice = 50 });

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_UpdateRecipeName_RenamesRecipe()
    {
        // Arrange
        var catalog = GivenCatalog(CatalogWithRecipe());
        var recipe = catalog.Recipes.Single();
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new UpdateServiceRecipeCommand(catalog.CatalogId, recipe.Id, Technician, "Breaker swap", null, null, null,
            null, null, null, null, null, null, null, null));

        // Assert
        Assert.Equal("Breaker swap", result!.ServiceName);
    }

    [Fact]
    public async Task Handle_UpdateRecipeInForeignCatalog_ThrowsUnauthorizedCatalogAccessException()
    {
        // Arrange
        var catalog = GivenCatalog(CatalogWithRecipe());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new UpdateServiceRecipeCommand(CatalogId.NewId(), catalog.Recipes.Single().Id, Technician, "x", null, null,
            null, null, null, null, null, null, null, null, null));

        // Assert
        await Assert.ThrowsAsync<UnauthorizedCatalogAccessException>(act);
    }

    [Fact]
    public async Task Handle_DeactivateOnlyRecipe_DeactivatesRecipeAndCatalog()
    {
        // Arrange
        var catalog = GivenCatalog(CatalogWithRecipe());
        var recipe = catalog.Recipes.Single();
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new DeactivateServiceRecipeCommand(catalog.CatalogId, recipe.Id, Technician, "seasonal"));

        // Assert
        Assert.True(result);
        Assert.Equal((false, ECatalogStatus.Inactive), (recipe.IsActive, catalog.Status));
    }

    [Fact]
    public async Task Handle_DeactivateRecipeWithServicesInProgress_ThrowsCannotDeactivateRecipeWithInProgressServicesException()
    {
        // Arrange
        var catalog = GivenCatalog(CatalogWithRecipe());
        _monitoring.Returns(nameof(IMonitoringContextFacade.CountInProgressServicesForRecipeAsync), 2);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new DeactivateServiceRecipeCommand(catalog.CatalogId, catalog.Recipes.Single().Id, Technician, "seasonal"));

        // Assert
        await Assert.ThrowsAsync<CannotDeactivateRecipeWithInProgressServicesException>(act);
    }

    [Fact]
    public async Task Handle_ReactivateDeactivatedRecipe_ReactivatesRecipeAndCatalog()
    {
        // Arrange
        var catalog = GivenCatalog(CatalogWithRecipe());
        var recipe = catalog.Recipes.Single();
        var sut = CreateSut();
        await sut.Handle(new DeactivateServiceRecipeCommand(catalog.CatalogId, recipe.Id, Technician, "seasonal"));

        // Act
        var result = await sut.Handle(new ReactivateServiceRecipeCommand(catalog.CatalogId, recipe.Id, Technician));

        // Assert
        Assert.True(result);
        Assert.Equal((true, ECatalogStatus.Active), (recipe.IsActive, catalog.Status));
    }

    [Fact]
    public async Task Handle_ReactivateUnknownRecipe_ThrowsRecipeNotFoundException()
    {
        // Arrange
        var catalog = GivenCatalog(CatalogWithRecipe());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new ReactivateServiceRecipeCommand(catalog.CatalogId, RecipeId.NewId(), Technician));

        // Assert
        await Assert.ThrowsAsync<RecipeNotFoundException>(act);
    }
}
