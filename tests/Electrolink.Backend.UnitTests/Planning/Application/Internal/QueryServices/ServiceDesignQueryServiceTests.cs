using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Assets.Interfaces.ACL;
using Hampcoders.Electrolink.API.Monitoring.Interfaces.ACL;
using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Processing.Interfaces.ACL;
using Hampcoders.Electrolink.API.Processing.Interfaces.ACL.Services;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.ACL;
using Microsoft.Extensions.Logging.Abstractions;
using static Electrolink.Backend.UnitTests.Planning.PlanningTestData;

namespace Electrolink.Backend.UnitTests.Planning.Application.Internal.QueryServices;

public class ServiceDesignQueryServiceTests
{
    private readonly Fake<IServiceCatalogRepository> _catalogs = new();
    private readonly Fake<IServiceRequestRepository> _requests = new();
    private readonly Fake<IProfilesContextFacade> _profiles = new();
    private readonly Fake<ISubscriptionContextFacade> _subscriptions = new();
    private readonly Fake<IAssetsContextFacade> _assets = new();

    private ServiceDesignQueryService CreateSut() => new(_catalogs.Object, _requests.Object, new Fake<IServiceAssignmentRepository>().Object,
        new ExternalProfilesService(_profiles.Object), new ExternalSubscriptionsService(_subscriptions.Object),
        new ExternalAssetsService(_assets.Object), NullLogger<ServiceDesignQueryService>.Instance);

    [Fact]
    public async Task Handle_GetCatalog_ReturnsTechnicianCatalog()
    {
        // Arrange
        var catalog = CatalogWithRecipe();
        _catalogs.Returns(nameof(IServiceCatalogRepository.FindByTechnicianIdAsync), catalog);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetServiceCatalogQuery(Technician));

        // Assert
        Assert.Same(catalog, result);
    }

    [Fact]
    public async Task Handle_GetRecipeDetailsWithoutCatalog_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetServiceRecipeDetailsQuery(RecipeId.NewId(), Technician));

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_GetRecipeDetailsForExistingRecipe_ReturnsRecipe()
    {
        // Arrange
        var catalog = CatalogWithRecipe();
        _catalogs.Returns(nameof(IServiceCatalogRepository.FindByTechnicianIdAsync), catalog);
        var recipe = catalog.Recipes.Single();
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetServiceRecipeDetailsQuery(recipe.Id, Technician));

        // Assert
        Assert.Same(recipe, result);
    }

    [Fact]
    public async Task Handle_GetEligibilityForInactiveClient_ReturnsProfileIncomplete()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetRequestEligibilityQuery(Client));

        // Assert
        Assert.Equal(new RequestEligibility(false, null, null, false, "PROFILE_INCOMPLETE"), result);
    }

    [Fact]
    public async Task Handle_GetEligibilityWhenLimitReached_ReturnsMonthlyLimitReason()
    {
        // Arrange
        _profiles.Returns(nameof(IProfilesContextFacade.IsHomeownerActiveAsync), true);
        _subscriptions.Returns(nameof(ISubscriptionContextFacade.GetRequestEligibilityAsync), (false, "FREE", (int?)0, false));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetRequestEligibilityQuery(Client));

        // Assert
        Assert.Equal(new RequestEligibility(false, "FREE", 0, false, "MONTHLY_LIMIT_REACHED"), result);
    }

    [Fact]
    public async Task Handle_GetEligibilityForEligibleClient_ReturnsNoReason()
    {
        // Arrange
        _profiles.Returns(nameof(IProfilesContextFacade.IsHomeownerActiveAsync), true);
        _subscriptions.Returns(nameof(ISubscriptionContextFacade.GetRequestEligibilityAsync), (true, "PRO", (int?)4, true));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetRequestEligibilityQuery(Client));

        // Assert
        Assert.Equal(new RequestEligibility(true, "PRO", 4, true, null), result);
    }

    [Fact]
    public async Task Handle_GetAvailableServicesWithoutGeolocation_ReturnsEmpty()
    {
        // Arrange
        _requests.Returns(nameof(IServiceRequestRepository.FindByIdAsync), Draft());
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetAvailableServicesQuery(RequestId.NewId()));

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_GetAvailableServicesWithStockedRecipes_AggregatesPerCategory()
    {
        // Arrange
        var other = TechnicianId.From("tech-2");
        _requests.Returns(nameof(IServiceRequestRepository.FindByIdAsync), WithProperty());
        _profiles.Returns(nameof(IProfilesContextFacade.GetTechniciansInAreaAsync),
            new List<(string, string, string)> { (Ids.Technician, "p1", "A"), ("tech-2", "p2", "B") });
        _catalogs.Returns(nameof(IServiceCatalogRepository.FindCatalogsByTechnicianIdsAsync), new Dictionary<TechnicianId, ServiceCatalog>
        {
            [Technician] = CatalogWithRecipe(price: 150),
            [other] = CatalogWithRecipe(technician: other, price: 250)
        });
        _assets.Returns(nameof(IAssetsContextFacade.CheckAllComponentsInStockAsync), true);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetAvailableServicesQuery(RequestId.NewId()));

        // Assert
        Assert.Equal(new AvailableService("Repair", "Reparación", 150, 250, 90, 2), Assert.Single(result));
    }

    [Fact]
    public async Task Handle_GetAvailableServicesWithoutStock_ExcludesRecipes()
    {
        // Arrange
        _requests.Returns(nameof(IServiceRequestRepository.FindByIdAsync), WithProperty());
        _profiles.Returns(nameof(IProfilesContextFacade.GetTechniciansInAreaAsync), new List<(string, string, string)> { (Ids.Technician, "p1", "A") });
        _catalogs.Returns(nameof(IServiceCatalogRepository.FindCatalogsByTechnicianIdsAsync),
            new Dictionary<TechnicianId, ServiceCatalog> { [Technician] = CatalogWithRecipe() });
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetAvailableServicesQuery(RequestId.NewId()));

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_GetSummaryForAnotherClient_ReturnsNull()
    {
        // Arrange
        _requests.Returns(nameof(IServiceRequestRepository.FindByIdAsync), Draft());
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetServiceRequestSummaryQuery(RequestId.NewId(), ClientIdentity.FromHomeowner("ho-2")));

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_GetSummaryForOwner_ReturnsRequest()
    {
        // Arrange
        var request = Draft();
        _requests.Returns(nameof(IServiceRequestRepository.FindByIdAsync), request);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetServiceRequestSummaryQuery(request.RequestId, Client));

        // Assert
        Assert.Same(request, result);
    }

    [Fact]
    public async Task Handle_GetMatchingQueue_SplitsPriorityAndNormalRequests()
    {
        // Arrange
        _requests.Returns(nameof(IServiceRequestRepository.FindPendingAssignmentAsync),
            new[] { PendingAssignment(), PendingAssignment(priority: true), PendingAssignment() });
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetMatchingQueueQuery());

        // Assert
        Assert.Equal((1, 2, 3), (result.PriorityQueue.Count, result.NormalQueue.Count, result.TotalPending));
    }

    [Fact]
    public async Task Handle_GetById_ReturnsRepositoryRequest()
    {
        // Arrange
        var request = Draft();
        _requests.Returns(nameof(IServiceRequestRepository.FindByIdAsync), request);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetServiceRequestByIdQuery(request.RequestId));

        // Assert
        Assert.Same(request, result);
    }
}
