using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Planning.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace Hampcoders.Electrolink.API.Planning.Interfaces.REST;

[ApiController]
[Route("api/v1/technicians/{technicianId}/catalog")]
[Produces("application/json")]
// M3.26 (user decision): the catalog is read and written only by its technician. The actor is the IAM user loaded by the
// middleware, resolved server-side through Profiles (no JWT claims); "me" or the actor's own id. Ineligible → 403, other technician → 404.
public class ServiceCatalogController(
    IServiceCatalogCommandService commandService,
    IServiceDesignQueryService queryService,
    ExternalProfilesService profiles)
    : ControllerBase
{
    private ObjectResult ResourceNotFound() => NotFound(new { message = "Resource not found." });

    private async Task<(TechnicianId? Technician, ProfileId? Profile, ActionResult? Denied)> AuthorizeAsync(string technicianId)
    {
        if (HttpContext.Items["User"] is not User actor) return (null, null, Unauthorized());
        if (actor.Status != EUserStatus.Active) return (null, null, StatusCode(StatusCodes.Status403Forbidden));
        if (await profiles.GetActiveTechnicianByUserIdAsync(actor.Id.Value) is not { } own) return (null, null, StatusCode(StatusCodes.Status403Forbidden));
        if (technicianId != "me" && technicianId != own.TechnicianId) return (null, null, ResourceNotFound());
        return (TechnicianId.From(own.TechnicianId), ProfileId.From(own.ProfileId), null);
    }
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCatalog([FromRoute] string technicianId)
    {
        var (technician, profile, denied) = await AuthorizeAsync(technicianId);
        if (denied is not null) return denied;
        try
        {
            var command = new CreateServiceCatalogCommand(technician!, profile!);
            await commandService.Handle(command);
            
            return StatusCode(StatusCodes.Status201Created);
        }
        catch (CatalogAlreadyExistsException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
    
    [HttpGet]
    [ProducesResponseType(typeof(ServiceCatalogResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceCatalogResource>> GetCatalog(
        [FromRoute] string technicianId)
    {
        var (technician, _, denied) = await AuthorizeAsync(technicianId);
        if (denied is not null) return denied;
        var catalog = await queryService.Handle(new GetServiceCatalogQuery(technician!));
        if (catalog is null) return NotFound(new { message = "Catalog not found" });
        return Ok(ServiceCatalogResourceFromEntityAssembler.ToResource(catalog));
    }

    [HttpPost("recipes")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateRecipe(
        [FromRoute] string technicianId,
        [FromBody]  CreateServiceRecipeResource resource)
    {
        var (technician, _, denied) = await AuthorizeAsync(technicianId);
        if (denied is not null) return denied;
        try
        {
            var catalog = await queryService.Handle(new GetServiceCatalogQuery(technician!));
            if (catalog is null) return NotFound(new { message = "Catalog not found" });

            var command = CreateServiceRecipeCommandFromResourceAssembler
                .ToCommand(resource, catalog.CatalogId.Value, technician!.Value);

            var recipe = await commandService.Handle(command);
            if (recipe is null) return BadRequest();
            return CreatedAtAction(
                nameof(GetRecipeDetails),
                new { technicianId = technician!.Value, recipeId = recipe.Id.Value },
                new { recipeId = recipe.Id.Value });
        }
        catch (DuplicateRecipeNameException ex) { return Conflict(new { message = ex.Message }); }
        catch (InvalidPricingException ex)      { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("recipes/{recipeId}")]
    [ProducesResponseType(typeof(ServiceRecipeDetailResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceRecipeDetailResource>> GetRecipeDetails(
        [FromRoute] string technicianId,
        [FromRoute] string recipeId)
    {
        var (technician, _, denied) = await AuthorizeAsync(technicianId);
        if (denied is not null) return denied;
        var catalog = await queryService.Handle(new GetServiceCatalogQuery(technician!));
        if (catalog is null) return NotFound(new { message = "Catalog not found" });

        var recipe = catalog.Recipes.FirstOrDefault(r => r.Id.Value == recipeId);
        if (recipe is null) return NotFound(new { message = "Recipe not found" });

        return Ok(ServiceRecipeDetailResourceFromEntityAssembler.ToResource(recipe));
    }

    [HttpPatch("recipes/{recipeId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRecipe(
        [FromRoute] string technicianId,
        [FromRoute] string recipeId,
        [FromBody]  UpdateServiceRecipeResource resource)
    {
        var (technician, _, denied) = await AuthorizeAsync(technicianId);
        if (denied is not null) return denied;
        try
        {
            var catalog = await queryService.Handle(new GetServiceCatalogQuery(technician!));
            if (catalog is null) return NotFound(new { message = "Catalog not found" });

            var command = UpdateServiceRecipeCommandFromResourceAssembler.ToCommandFromResource(
                resource, 
                recipeId, 
                catalog.CatalogId.Value, 
                technician!.Value);
            
            await commandService.Handle(command);
            return NoContent();
        }
        catch (CannotChangeComponentsWithActiveServicesException ex) { return BadRequest(new { message = ex.Message }); }
        catch (PriceIncreaseTooLargeException ex) { return BadRequest(new { message = ex.Message }); }
        catch (RecipeNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpDelete("recipes/{recipeId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeactivateRecipe(
        [FromRoute] string technicianId,
        [FromRoute] string recipeId,
        [FromBody]  DeactivateServiceRecipeResource resource)
    {
        var (technician, _, denied) = await AuthorizeAsync(technicianId);
        if (denied is not null) return denied;
        try
        {
            var catalog = await queryService.Handle(new GetServiceCatalogQuery(technician!));
            if (catalog is null) return NotFound(new { message = "Catalog not found" });

            await commandService.Handle(new DeactivateServiceRecipeCommand(
                catalog.CatalogId, 
                RecipeId.From(recipeId), 
                technician!, 
                resource.Reason, 
                resource.Notes));
            
            return NoContent();
        }
        catch (CannotDeactivateRecipeWithInProgressServicesException ex) { return Conflict(new { message = ex.Message }); }
        catch (RecipeNotFoundException ex)                               { return NotFound(new { message = ex.Message }); }
    }

    [HttpPost("recipes/{recipeId}/reactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReactivateRecipe(
        [FromRoute] string technicianId,
        [FromRoute] string recipeId)
    {
        var (technician, _, denied) = await AuthorizeAsync(technicianId);
        if (denied is not null) return denied;
        try
        {
            var catalog = await queryService.Handle(new GetServiceCatalogQuery(technician!));
            if (catalog is null) return NotFound(new { message = "Catalog not found" });

            await commandService.Handle(new ReactivateServiceRecipeCommand(
                catalog.CatalogId, 
                RecipeId.From(recipeId), 
                technician!));
            return NoContent();
        }
        catch (RecipeNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }
}