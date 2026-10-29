using System.Net.Mime;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Assets.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Assets.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Hampcoders.Electrolink.API.Assets.Interfaces.REST;

[ApiController]
[Route("api/v1/homeowners/{ownerId}/[controller]")]
[Route("api/v1/companies/{ownerId}/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Property Portfolio Endpoints")]
// M3.23: the route owner must be the authenticated actor's own HomeOwner/Company subject (PropertyOwnerRoute);
// a property of another owner, or a missing portfolio/property/entry, is a generic 404.
public class PropertyPortfoliosController(
    IPropertyPortfolioCommandService commandService,
    IPropertyPortfolioQueryService queryService,
    IProfilesContextFacade profiles) : ControllerBase
{
    /// <summary>Retorna el portfolio del homeowner</summary>
    [HttpGet]
    [SwaggerOperation(Summary = "Get portfolio by owner ID", OperationId = "GetPortfolioByOwnerId")]
    [SwaggerResponse(StatusCodes.Status200OK, "Portfolio found", typeof(PropertyPortfolioResource))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Portfolio not found")]
    public async Task<IActionResult> GetPortfolio([FromRoute] string ownerId)
    {
        var (owner, denied) = await PropertyOwnerRoute.AuthorizeAsync(this, profiles, ownerId);
        if (denied is not null) return denied;
        var portfolio = await queryService.Handle(new GetPortfolioByOwnerIdQuery(owner!));
        if (portfolio is null) return PropertyOwnerRoute.NotFound(this);

        return Ok(PropertyPortfolioResourceFromEntityAssembler.ToResourceFromEntity(portfolio));
    }

    /// <summary>Agrega una propiedad al portfolio del homeowner</summary>
    [HttpPost("properties")]
    [SwaggerOperation(Summary = "Add property to portfolio", OperationId = "AddPropertyToPortfolio")]
    [SwaggerResponse(StatusCodes.Status200OK, "Property added to portfolio", typeof(PropertyPortfolioResource))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "Property already in portfolio or nickname in use")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Portfolio or property not found")]
    public async Task<IActionResult> AddPropertyToPortfolio(
        [FromRoute] string ownerId,
        [FromBody] AddPropertyToPortfolioResource resource)
    {
        var (owner, denied) = await PropertyOwnerRoute.AuthorizeAsync(this, profiles, ownerId);
        if (denied is not null) return denied;
        try
        {
            var command = AddPropertyToPortfolioCommandFromResourceAssembler.ToCommandFromResource(resource, owner!.ClientId);
            var portfolio = await commandService.Handle(command);
            if (portfolio is null) return BadRequest();
            return Ok(PropertyPortfolioResourceFromEntityAssembler.ToResourceFromEntity(portfolio));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        // Another owner's property: indistinguishable from a missing one (was 403 naming the owner).
        catch (UnauthorizedAccessException) { return PropertyOwnerRoute.NotFound(this); }
        catch (AssetNotFoundException) { return PropertyOwnerRoute.NotFound(this); }
    }

    /// <summary>Elimina una propiedad del portfolio del homeowner</summary>
    [HttpDelete("properties/{propertyId}")]
    [SwaggerOperation(Summary = "Remove property from portfolio", OperationId = "RemovePropertyFromPortfolio")]
    [SwaggerResponse(StatusCodes.Status204NoContent, "Property removed from portfolio")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Portfolio or property not found")]
    public async Task<IActionResult> RemovePropertyFromPortfolio(
        [FromRoute] string ownerId,
        [FromRoute] string propertyId,
        [FromQuery] string reason)
    {
        var (owner, denied) = await PropertyOwnerRoute.AuthorizeAsync(this, profiles, ownerId);
        if (denied is not null) return denied;
        if (!propertyId.StartsWith("prop-", StringComparison.Ordinal)) return PropertyOwnerRoute.NotFound(this);
        try
        {
            var command = RemovePropertyFromPortfolioCommandFromResourceAssembler.ToCommandFromResource(owner!.ClientId, propertyId, reason);
            var result = await commandService.Handle(command);
            return result ? NoContent() : NotFound();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (AssetNotFoundException) { return PropertyOwnerRoute.NotFound(this); }
    }
}