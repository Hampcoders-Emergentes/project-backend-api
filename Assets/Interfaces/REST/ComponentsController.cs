using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
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

/// <summary>
/// Controller for managing components.
/// </summary>
[ApiController]
[Route("api/v1/technicians/{technicianId}/[controller]")]
[SwaggerTag("Components Management")]
// M3.25: global catalogue; see ComponentCatalogAccess for the read/write policy.
public class ComponentsController(
    IComponentCommandService componentCommandService,
    IComponentQueryService componentQueryService,
    IProfilesContextFacade profiles) : ControllerBase
{
    [HttpPost]
    [SwaggerOperation(Summary = "Create Component", OperationId = "CreateComponent")]
    [SwaggerResponse(StatusCodes.Status201Created, "Component created", typeof(ComponentResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Component could not be created")]
    public async Task<IActionResult> CreateComponent(
        [FromRoute] string technicianId,  
        [FromBody] CreateComponentResource resource)
    {
        if (ComponentCatalogAccess.AuthorizeWrite(this) is { } denied) return denied;
        try
        {
            var command = CreateComponentCommandFromResourceAssembler.ToCommandFromResource(resource);
            var component = await componentCommandService.Handle(command);
            if (component is null) return BadRequest();

            return Ok(ComponentResourceFromEntityAssembler.ToResourceFromEntity(component));
        }
        catch (DuplicateAssetException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpGet("{componentId}", Name = nameof(GetComponentById))]
    [SwaggerOperation(Summary = "Get Component by Id", OperationId = "GetComponentById")]
    [SwaggerResponse(StatusCodes.Status200OK, "Component found", typeof(ComponentResource))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Component not found")]
    public async Task<IActionResult> GetComponentById(
        [FromRoute] string technicianId,   
        [FromRoute] string componentId)
    {
        var denied = await ComponentCatalogAccess.AuthorizeReadAsync(this, profiles, technicianId);
        if (denied is not null) return denied;
        var query = new GetComponentByIdQuery(ComponentId.From(componentId));
        var component = await componentQueryService.Handle(query);
        if (component is null) return NotFound();

        return Ok(ComponentResourceFromEntityAssembler.ToResourceFromEntity(component));
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Get All Components", OperationId = "GetAllComponents")]
    [SwaggerResponse(StatusCodes.Status200OK, "List of components retrieved", typeof(IEnumerable<ComponentResource>))]
    public async Task<IActionResult> GetAllComponents([FromRoute] string technicianId)  
    {
        var denied = await ComponentCatalogAccess.AuthorizeReadAsync(this, profiles, technicianId);
        if (denied is not null) return denied;
        var query = new GetAllComponentsQuery();
        var components = await componentQueryService.Handle(query);
        var resources = components.Select(ComponentResourceFromEntityAssembler.ToResourceFromEntity);
        return Ok(resources);
    }

    [HttpPut("{componentId}")]
    [SwaggerOperation(Summary = "Update Component", OperationId = "UpdateComponent")]
    [SwaggerResponse(StatusCodes.Status200OK, "Component updated", typeof(ComponentResource))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Component not found")]
    public async Task<IActionResult> UpdateComponent(
        [FromRoute] string technicianId,   
        [FromRoute] string componentId,
        [FromBody] UpdateComponentResource resource)
    {
        if (ComponentCatalogAccess.AuthorizeWrite(this) is { } denied) return denied;
        try
        {
            var command = UpdateComponentCommandFromResourceAssembler.ToCommandFromResource(resource, componentId);
            var component = await componentCommandService.Handle(command);
            if (component is null) return NotFound("Component not found");

            return Ok(ComponentResourceFromEntityAssembler.ToResourceFromEntity(component));
        }
        catch (AssetNotFoundException) { return NotFound("Component not found"); }
    }

    [HttpDelete("{componentId}")]
    [SwaggerOperation(Summary = "Delete Component", OperationId = "DeleteComponent")]
    [SwaggerResponse(StatusCodes.Status204NoContent, "Component deleted")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Component not found")]
    public async Task<IActionResult> DeleteComponent(
        [FromRoute] string technicianId,   
        [FromRoute] string componentId)
    {
        if (ComponentCatalogAccess.AuthorizeWrite(this) is { } denied) return denied;
        var command = new DeleteComponentCommand(ComponentId.From(componentId));
        var result = await componentCommandService.Handle(command);
        if (!result) return NotFound("Component not found");

        return NoContent();
    }
}