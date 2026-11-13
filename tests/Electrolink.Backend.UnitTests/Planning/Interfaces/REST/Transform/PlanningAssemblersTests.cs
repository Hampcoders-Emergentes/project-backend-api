using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Planning.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Interfaces.REST.Resources;
using static Electrolink.Backend.UnitTests.Planning.PlanningTestData;

namespace Electrolink.Backend.UnitTests.Planning.Interfaces.REST.Transform;

public class PlanningCommandAssemblersTests
{
    private static readonly string Catalog = CatalogId.NewId().Value;

    [Fact]
    public void CreateServiceRecipe_ToCommand_MapsPricingAndRequirements()
    {
        // Arrange
        var resource = new CreateServiceRecipeResource("Repair", "Fix", "Repair", true, [new ComponentRequirementResource("ctype-1", 2, true)],
            1, 30, new PricingResource(20, 80, 100, "PEN"), ["power off"], ["report"], 6);

        // Act
        var command = CreateServiceRecipeCommandFromResourceAssembler.ToCommand(resource, Catalog, Ids.Technician);

        // Assert
        Assert.Equal((Catalog, 100m, "PEN", 2, string.Empty), (command.CatalogId.Value, command.TotalPrice, command.Currency,
            command.ComponentRequirements.Single().Quantity, command.ComponentRequirements.Single().ComponentTypeName));
    }

    [Fact]
    public void CreateServiceRecipe_ToCommandWithoutRequirements_UsesEmptyList()
    {
        // Arrange
        var resource = new CreateServiceRecipeResource("Repair", "Fix", "Repair", false, null!, 1, 0, new PricingResource(20, 80, 100, "PEN"), [], [], 6);

        // Act
        var command = CreateServiceRecipeCommandFromResourceAssembler.ToCommand(resource, Catalog, Ids.Technician);

        // Assert
        Assert.Empty(command.ComponentRequirements);
    }

    [Fact]
    public void CreateServiceRecipe_ToCommandWithEmptyCatalogId_ThrowsArgumentException()
    {
        // Arrange
        var resource = new CreateServiceRecipeResource("Repair", "Fix", "Repair", false, [], 1, 0, new PricingResource(20, 80, 100, "PEN"), [], [], 6);

        // Act
        var act = () => CreateServiceRecipeCommandFromResourceAssembler.ToCommand(resource, " ", Ids.Technician);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void ExecuteMatchingAlgorithm_ToCommand_ParsesRequestId()
    {
        // Act
        var command = ExecuteMatchingAlgorithmCommandFromResourceAssembler.ToCommand(new ExecuteMatchingAlgorithmResource("req-7"));

        // Assert
        Assert.Equal(RequestId.From("req-7"), command.RequestId);
    }

    [Fact]
    public void ExecuteMatchingAlgorithm_ToCommandWithMalformedId_ThrowsArgumentException()
    {
        // Act
        var act = () => ExecuteMatchingAlgorithmCommandFromResourceAssembler.ToCommand(new ExecuteMatchingAlgorithmResource("7"));

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void UpdateServiceRecipe_ToCommandFromResourceWithoutPricing_LeavesPricingNull()
    {
        // Arrange
        var resource = new UpdateServiceRecipeResource("New", null, null, 2, null, null, null, null, null);
        var recipe = RecipeId.NewId().Value;

        // Act
        var command = UpdateServiceRecipeCommandFromResourceAssembler.ToCommandFromResource(resource, recipe, Catalog, Ids.Technician);

        // Assert
        Assert.Equal(("New", (decimal?)null, (string?)null, 2), (command.ServiceName, command.TotalPrice, command.Currency,
            command.EstimatedDurationHours!.Value));
        Assert.Null(command.ComponentRequirements);
    }

    [Fact]
    public void UpdateServiceRecipe_ToCommandFromResourceWithPricing_MapsPricingAndRequirements()
    {
        // Arrange
        var resource = new UpdateServiceRecipeResource(null, null, [new ComponentRequirementResource("ctype-2", 1, false)], null, null,
            new PricingResource(10, 20, 40, "USD"), null, null, 12);

        // Act
        var command = UpdateServiceRecipeCommandFromResourceAssembler.ToCommandFromResource(resource, RecipeId.NewId().Value, Catalog,
            Ids.Technician);

        // Assert
        Assert.Equal((40m, "USD", "ctype-2", 12), (command.TotalPrice!.Value, command.Currency, command.ComponentRequirements!.Single().ComponentTypeId,
            command.WarrantyMonths!.Value));
    }
}

public class PlanningResourceAssemblersTests
{
    [Fact]
    public void MatchingQueueResource_ToResource_MapsBothQueues()
    {
        // Arrange
        var at = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        // Act
        var resource = MatchingQueueResourceFromQueryResultAssembler.ToResource([("req-1", Homeowner, true, at)],
            [("req-2", Homeowner, false, at), ("req-3", Homeowner, false, at)], 3);

        // Assert
        Assert.Equal((1, 2, 3, "req-1"), (resource.PriorityQueue.Count, resource.NormalQueue.Count, resource.TotalPending,
            resource.PriorityQueue[0].RequestId));
    }

    [Fact]
    public void MatchingResultResource_ToResourceWithoutFailure_LeavesReasonNull()
    {
        // Act
        var resource = MatchingResultResourceAssembler.ToResource("req-1", true);

        // Assert
        Assert.Equal(new MatchingResultResource("req-1", true, null), resource);
    }

    [Fact]
    public void MatchingScoreResource_ToResource_MapsScoreMethodAndReasoning()
    {
        // Act
        var resource = MatchingScoreResourceAssembler.ToResource(MatchingScore.AIEnhanced(0.82, "closest expert"));

        // Assert
        Assert.Equal(new MatchingScoreResource(0.82, "AIEnhanced", "closest expert"), resource);
    }

    [Fact]
    public void ServiceAssignmentResource_ToResourceFromEntity_MapsTechnicianSnapshotAndScore()
    {
        // Arrange
        var snapshot = RecipeSnapshot.FromRecipe(Recipe());
        var assignment = ServiceAssignment.Assign(RequestId.NewId(), Technician, snapshot,
            MatchingCriteria.Create(Location, ["ctype-1"], false, false, Technician), MatchingScore.Deterministic(0.7));

        // Act
        var resource = ServiceAssignmentResourceFromEntityAssembler.ToResourceFromEntity(assignment);

        // Assert
        Assert.Equal((Ids.Technician, "Breaker repair", 150m, "Pen", 0.7), (resource.TechnicianId, resource.RecipeSnapshot.ServiceName,
            resource.RecipeSnapshot.TotalPrice, resource.RecipeSnapshot.Currency, resource.MatchingDetails!.Score));
    }

    [Fact]
    public void ServiceAssignmentResource_ToResourceFromFailedAssignment_HasNoTechnicianOrSnapshot()
    {
        // Arrange
        var assignment = ServiceAssignment.Fail(RequestId.NewId(), "NO_CANDIDATES_AVAILABLE", 1);

        // Act
        var resource = ServiceAssignmentResourceFromEntityAssembler.ToResourceFromEntity(assignment);

        // Assert
        Assert.Equal((string.Empty, (RecipeSnapshotResource?)null, "NO_CANDIDATES_AVAILABLE"),
            (resource.TechnicianId, resource.RecipeSnapshot, resource.FailureReason));
    }

    [Fact]
    public void ServiceCatalogResource_ToResource_SummarizesRecipes()
    {
        // Arrange
        var catalog = CatalogWithRecipe();

        // Act
        var resource = ServiceCatalogResourceFromEntityAssembler.ToResource(catalog);

        // Assert
        var recipe = Assert.Single(resource.Recipes);
        Assert.Equal(("Active", 1, "Breaker repair", 90, true), (resource.Status, resource.TotalRecipes, recipe.ServiceName,
            recipe.EstimatedDurationMinutes, recipe.IsActive));
    }

    [Fact]
    public void ServiceRecipeDetailResource_ToResource_MapsPricingBreakdown()
    {
        // Arrange
        var recipe = Recipe();

        // Act
        var resource = ServiceRecipeDetailResourceFromEntityAssembler.ToResource(recipe);

        // Assert
        Assert.Equal((50m, 100m, 150m, 6, "ctype-1"), (resource.MaterialsEstimate, resource.LaborCost, resource.TotalPrice, resource.WarrantyMonths,
            resource.ComponentRequirements.Single().ComponentTypeId));
    }

    [Fact]
    public void ServiceRequestSummaryResource_ToResourceForDraft_LeavesSelectionsNull()
    {
        // Arrange
        var request = Draft();

        // Act
        var resource = ServiceRequestSummaryResourceFromEntityAssembler.ToResource(request);

        // Assert
        Assert.Equal((Homeowner, (string?)null, (string?)null, "Draft"), (resource.OwnerId, resource.PropertyId, resource.SelectedTechnicianId,
            resource.Status));
    }

    [Fact]
    public void ServiceRequestSummaryResource_ToResourceForAssigned_MapsTechnicianAndRecipe()
    {
        // Arrange
        var request = Assigned();

        // Act
        var resource = ServiceRequestSummaryResourceFromEntityAssembler.ToResource(request);

        // Assert
        Assert.Equal((Ids.Technician, request.SelectedRecipeId!.Value, "Assigned"), (resource.SelectedTechnicianId, resource.SelectedRecipeId,
            resource.Status));
    }

    [Fact]
    public void ServiceSuggestionResource_ToResource_MapsSuggestion()
    {
        // Arrange
        var expires = DateTime.UtcNow.AddDays(2);
        var suggestion = ServiceSuggestion.Create(Homeowner, null, "prop-1", "an-1", "dev-1", "SPIKE", "HIGH", expires);

        // Act
        var resource = ServiceSuggestionResourceFromEntityAssembler.ToResource(suggestion);

        // Assert
        Assert.Equal(new ServiceSuggestionResource(suggestion.SuggestionId, "prop-1", "SPIKE", "HIGH", "Pending", expires, null), resource);
    }
}
