using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Assets.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Assets.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Microsoft.AspNetCore.Mvc;
using Hampcoders.Electrolink.API.Shared.Infrastructure.OpenApi;

namespace Hampcoders.Electrolink.API.Assets.Interfaces.REST;

/// <summary>
/// Controller for managing component types.
/// </summary>
[ApiController]
[Route("api/v1/technicians/{technicianId}/[controller]")]
[ApiDocTag("Component Types Management")]
// M3.25: global catalogue; see ComponentCatalogAccess for the read/write policy.
public class ComponentTypesController(
    IComponentTypeCommandService componentTypeCommandService,
    IComponentTypeQueryService componentTypeQueryService,
    IProfilesContextFacade profiles) : ControllerBase
{
    [HttpPost]
    [ApiDocOperation(Summary = "Create Component Type", OperationId = "CreateComponentType")]
    [ApiDocResponse(StatusCodes.Status201Created, "Component type created", typeof(ComponentTypeResource))]
    [ApiDocResponse(StatusCodes.Status400BadRequest, "Component type could not be created")]
    public async Task<IActionResult> CreateComponentType(
        [FromRoute] string technicianId,
        [FromBody] CreateComponentTypeResource resource)
    {
        if (ComponentCatalogAccess.AuthorizeWrite(this) is { } denied) return denied;
        try
        {
            var command = CreateComponentTypeCommandFromResourceAssembler.ToCommandFromResource(resource);
            var componentType = await componentTypeCommandService.Handle(command);
            if (componentType is null) return BadRequest();

            return Ok(ComponentTypeResourceFromEntityAssembler.ToResourceFromEntity(componentType));
        }
        catch (DuplicateAssetException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpGet("{typeId}", Name = nameof(GetComponentTypeById))]
    [ApiDocOperation(Summary = "Get Component Type by Id", OperationId = "GetComponentTypeById")]
    [ApiDocResponse(StatusCodes.Status200OK, "Component type found", typeof(ComponentTypeResource))]
    [ApiDocResponse(StatusCodes.Status404NotFound, "Component type not found")]
    public async Task<IActionResult> GetComponentTypeById(
        [FromRoute] string technicianId,  
        [FromRoute] string typeId)
    {
        var denied = await ComponentCatalogAccess.AuthorizeReadAsync(this, profiles, technicianId);
        if (denied is not null) return denied;
        var query = new GetComponentTypeByIdQuery(ComponentTypeId.From(typeId));
        var componentType = await componentTypeQueryService.Handle(query);
        if (componentType is null) return NotFound();

        return Ok(ComponentTypeResourceFromEntityAssembler.ToResourceFromEntity(componentType));
    }

    [HttpGet]
    [ApiDocOperation(Summary = "Get All Component Types", OperationId = "GetAllComponentTypes")]
    [ApiDocResponse(StatusCodes.Status200OK, "List of component types retrieved", typeof(IEnumerable<ComponentTypeResource>))]
    public async Task<IActionResult> GetAllComponentTypes([FromRoute] string technicianId)  
    {
        var denied = await ComponentCatalogAccess.AuthorizeReadAsync(this, profiles, technicianId);
        if (denied is not null) return denied;
        var query = new GetAllComponentTypesQuery();
        var componentTypes = await componentTypeQueryService.Handle(query);
        var resources = componentTypes.Select(ComponentTypeResourceFromEntityAssembler.ToResourceFromEntity);
        return Ok(resources);
    }

    [HttpPut("{typeId}")]
    [ApiDocOperation(Summary = "Update Component Type", OperationId = "UpdateComponentType")]
    [ApiDocResponse(StatusCodes.Status200OK, "Component type updated", typeof(ComponentTypeResource))]
    [ApiDocResponse(StatusCodes.Status404NotFound, "Component type not found")]
    public async Task<IActionResult> UpdateComponentType(
        [FromRoute] string technicianId, 
        [FromRoute] string typeId,
        [FromBody] UpdateComponentTypeResource resource)
    {
        if (ComponentCatalogAccess.AuthorizeWrite(this) is { } denied) return denied;
        try
        {
            var command = UpdateComponentTypeCommandFromResourceAssembler.ToCommandFromResource(resource, typeId);
            var componentType = await componentTypeCommandService.Handle(command);
            if (componentType is null) return NotFound("Component Type not found");

            return Ok(ComponentTypeResourceFromEntityAssembler.ToResourceFromEntity(componentType));
        }
        catch (AssetNotFoundException) { return NotFound("Component Type not found"); }
    }

    [HttpDelete("{typeId}")]
    [ApiDocOperation(Summary = "Delete Component Type", OperationId = "DeleteComponentType")]
    [ApiDocResponse(StatusCodes.Status204NoContent, "Component type deleted")]
    [ApiDocResponse(StatusCodes.Status404NotFound, "Component type not found")]
    public async Task<IActionResult> DeleteComponentType(
        [FromRoute] string technicianId,  
        [FromRoute] string typeId)
    {
        if (ComponentCatalogAccess.AuthorizeWrite(this) is { } denied) return denied;
        try
        {
            var command = new DeleteComponentTypeCommand(ComponentTypeId.From(typeId));
            var result = await componentTypeCommandService.Handle(command);
            if (!result) return NotFound("Component Type not found");

            return NoContent();
        }
        // A type referenced by technician stock cannot be deleted.
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPatch("{typeId}/activate")]
    [ApiDocOperation(Summary = "Activate Component Type", OperationId = "ActivateComponentType")]
    [ApiDocResponse(StatusCodes.Status200OK, "Component type activated", typeof(ComponentTypeResource))]
    [ApiDocResponse(StatusCodes.Status404NotFound, "Component type not found")]
    [ApiDocResponse(StatusCodes.Status409Conflict, "Component type is already active")]
    public async Task<IActionResult> ActivateComponentType(
        [FromRoute] string technicianId,
        [FromRoute] string typeId)
    {
        if (ComponentCatalogAccess.AuthorizeWrite(this) is { } denied) return denied;
        try
        {
            var command = new ActivateComponentTypeCommand(ComponentTypeId.From(typeId));
            var componentType = await componentTypeCommandService.Handle(command);
            if (componentType is null) return NotFound();
            return Ok(ComponentTypeResourceFromEntityAssembler.ToResourceFromEntity(componentType));
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (AssetNotFoundException) { return NotFound(); }
    }

    [HttpPatch("{typeId}/deactivate")]
    [ApiDocOperation(Summary = "Deactivate Component Type", OperationId = "DeactivateComponentType")]
    [ApiDocResponse(StatusCodes.Status200OK, "Component type deactivated", typeof(ComponentTypeResource))]
    [ApiDocResponse(StatusCodes.Status404NotFound, "Component type not found")]
    [ApiDocResponse(StatusCodes.Status409Conflict, "Component type is already inactive")]
    public async Task<IActionResult> DeactivateComponentType(
        [FromRoute] string technicianId,
        [FromRoute] string typeId)
    {
        if (ComponentCatalogAccess.AuthorizeWrite(this) is { } denied) return denied;
        try
        {
            var command = new DeactivateComponentTypeCommand(ComponentTypeId.From(typeId));
            var componentType = await componentTypeCommandService.Handle(command);
            if (componentType is null) return NotFound();
            return Ok(ComponentTypeResourceFromEntityAssembler.ToResourceFromEntity(componentType));
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (AssetNotFoundException) { return NotFound(); }
    }
}
