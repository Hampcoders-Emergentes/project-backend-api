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

namespace Electrolink.Backend.UnitTests.Planning.Application.Internal.OutboundServices;

public class PlanningExternalServicesTests
{
    [Fact]
    public async Task ExternalAssetsService_ReserveComponentsAsync_ReturnsFacadeResult()
    {
        // Arrange
        var assets = new Fake<IAssetsContextFacade>().Returns(nameof(IAssetsContextFacade.ReserveComponentsForServiceAsync), true);
        var sut = new ExternalAssetsService(assets.Object);

        // Act
        var result = await sut.ReserveComponentsAsync(Ids.Technician, "assign-1", [("ctype-1", 2)]);

        // Assert
        Assert.True(result);
        Assert.Equal("assign-1", assets.Calls.Single().Arg<string>(1));
    }

    [Fact]
    public async Task ExternalAssetsService_ReleaseReservationAsync_ForwardsReason()
    {
        // Arrange
        var assets = new Fake<IAssetsContextFacade>();
        var sut = new ExternalAssetsService(assets.Object);

        // Act
        await sut.ReleaseReservationAsync(Ids.Technician, "assign-1", "cancelled");

        // Assert
        Assert.Equal("cancelled", assets.Calls.Single().Arg<string>(2));
    }

    [Fact]
    public async Task ExternalAssetsService_HomeownerHasPropertiesAsync_ReturnsFacadeResult()
    {
        // Arrange
        var assets = new Fake<IAssetsContextFacade>().Returns(nameof(IAssetsContextFacade.HomeownerHasPropertiesAsync), true);
        var sut = new ExternalAssetsService(assets.Object);

        // Act
        var result = await sut.HomeownerHasPropertiesAsync(Homeowner);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ExternalMonitoringService_CountActiveServicesForRecipeAsync_ReturnsFacadeCount()
    {
        // Arrange
        var monitoring = new Fake<IMonitoringContextFacade>().Returns(nameof(IMonitoringContextFacade.CountActiveServicesForRecipeAsync), 3);
        var sut = new ExternalMonitoringService(monitoring.Object);

        // Act
        var result = await sut.CountActiveServicesForRecipeAsync("recipe-1");

        // Assert
        Assert.Equal(3, result);
    }

    [Fact]
    public async Task ExternalMonitoringService_CreateServiceExecutionAsync_ForwardsAssignmentData()
    {
        // Arrange
        var monitoring = new Fake<IMonitoringContextFacade>();
        var sut = new ExternalMonitoringService(monitoring.Object);
        var snapshot = RecipeSnapshot.FromRecipe(Recipe());

        // Act
        await sut.CreateServiceExecutionAsync("assign-1", "req-1", Ids.Technician, Homeowner, "prop-1", snapshot, DateTime.UtcNow, true);

        // Assert
        var args = monitoring.Calls.Single().Args;
        Assert.Equal(("assign-1", Homeowner, snapshot, true), (args[0], args[3], args[5], args[7]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExternalSubscriptionsService_CanCreateRequestAsync_ReturnsEligibilityFlag(bool canCreate)
    {
        // Arrange
        var subscriptions = new Fake<ISubscriptionContextFacade>()
            .Returns(nameof(ISubscriptionContextFacade.GetRequestEligibilityAsync), (canCreate, "PRO", (int?)1, false));
        var sut = new ExternalSubscriptionsService(subscriptions.Object);

        // Act
        var result = await sut.CanCreateRequestAsync(Homeowner);

        // Assert
        Assert.Equal(canCreate, result);
    }

    [Fact]
    public async Task ExternalSubscriptionsService_GetPlanTypeAsync_ReturnsPlanType()
    {
        // Arrange
        var subscriptions = new Fake<ISubscriptionContextFacade>()
            .Returns(nameof(ISubscriptionContextFacade.GetRequestEligibilityAsync), (true, "ENTERPRISE", (int?)null, true));
        var sut = new ExternalSubscriptionsService(subscriptions.Object);

        // Act
        var result = await sut.GetPlanTypeAsync(Company);

        // Assert
        Assert.Equal("ENTERPRISE", result);
    }

    [Fact]
    public async Task ExternalSubscriptionsService_CanMarkAsPriorityAsync_ReturnsPriorityFlag()
    {
        // Arrange
        var subscriptions = new Fake<ISubscriptionContextFacade>()
            .Returns(nameof(ISubscriptionContextFacade.GetRequestEligibilityAsync), (true, "PRO", (int?)2, true));
        var sut = new ExternalSubscriptionsService(subscriptions.Object);

        // Act
        var result = await sut.CanMarkAsPriorityAsync(Homeowner);

        // Assert
        Assert.True(result);
    }
}
