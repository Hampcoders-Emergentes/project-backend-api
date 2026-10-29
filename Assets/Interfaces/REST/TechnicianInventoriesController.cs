using System.Net.Mime;
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

[ApiController]
[Route("api/v1/technicians/{technicianId}/inventory")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Technician Inventory Endpoints")]
// M3.24: the route technicianId must be the authenticated technician's own subject (TechnicianRoute).
public class TechnicianInventoriesController(ITechnicianInventoryCommandService inventoryCommandService, ITechnicianInventoryQueryService inventoryQueryService,
    IProfilesContextFacade profiles) : ControllerBase
{
    [HttpPost]
    [SwaggerOperation(Summary = "Create technician inventory", OperationId = "CreateTechnicianInventory")]
    [SwaggerResponse(StatusCodes.Status201Created, "Inventory created successfully")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "Inventory already exists for this technician")]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Could not create inventory")]
    public async Task<IActionResult> CreateTechnicianInventory([FromRoute] string technicianId)
    {
        var (technician, denied) = await TechnicianRoute.AuthorizeAsync(this, profiles, technicianId);
        if (denied is not null) return denied;
        try
        {
            var command = new CreateTechnicianInventoryCommand(technician!);
            var inventory = await inventoryCommandService.Handle(command);
            if (inventory is null) return BadRequest("Could not create inventory.");

            var response = TechnicianInventoryResourceFromEntityAssembler.ToResourceFromEntity(inventory);
            return CreatedAtRoute(nameof(GetInventoryByTechnicianId), new { technicianId }, response);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (DuplicateAssetException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPost("stock-items")]
    [SwaggerOperation(Summary = "Add stock item to technician inventory", OperationId = "AddStockItemToInventory")]
    [SwaggerResponse(StatusCodes.Status200OK, "Stock item added successfully", typeof(TechnicianInventoryResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Failed to add stock item")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Inventory not found")]
    public async Task<IActionResult> AddStockItemToInventory([FromRoute] string technicianId, [FromBody] AddStockToInventoryResource resource)
    {
        var (technician, denied) = await TechnicianRoute.AuthorizeAsync(this, profiles, technicianId);
        if (denied is not null) return denied;
        try
        {
            var command = AddStockToInventoryCommandFromResourceAssembler.ToCommandFromResource(resource, technicianId);
            var inventory = await inventoryCommandService.Handle(command);
            if (inventory is null) return BadRequest();
            var response = await BuildResponseAsync(technicianId);
            return Ok(response);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (AssetNotFoundException) { return PropertyOwnerRoute.NotFound(this); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPut("{componentId}")]
    [SwaggerOperation(Summary = "Update component stock in technician inventory", OperationId = "UpdateComponentStock")]
    [SwaggerResponse(StatusCodes.Status200OK, "Inventory updated", typeof(TechnicianInventoryResource))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Inventory or component not found")]
    public async Task<IActionResult> UpdateComponentStock(
        [FromRoute] string technicianId,
        [FromRoute] string componentId,
        [FromBody] UpdateComponentStockResource resource)
    {
        var (technician, denied) = await TechnicianRoute.AuthorizeAsync(this, profiles, technicianId);
        if (denied is not null) return denied;
        try
        {
            var command = UpdateComponentStockCommandFromResourceAssembler.ToCommandFromResource(resource, technicianId, componentId);
            var inventory = await inventoryCommandService.Handle(command);
            if (inventory is null) return NotFound();

            var response = await BuildResponseAsync(technicianId);
            return Ok(response);
        }
        catch (KeyNotFoundException) { return PropertyOwnerRoute.NotFound(this); }
        catch (AssetNotFoundException) { return PropertyOwnerRoute.NotFound(this); }
    }

    [HttpDelete("{componentId}")]
    [SwaggerOperation(Summary = "Remove component from technician inventory", OperationId = "RemoveComponentStock")]
    [SwaggerResponse(StatusCodes.Status204NoContent, "Component removed from inventory")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Component or inventory not found")]
    public async Task<IActionResult> RemoveComponentStock([FromRoute] string technicianId, [FromRoute] string componentId)
    {
        var (technician, denied) = await TechnicianRoute.AuthorizeAsync(this, profiles, technicianId);
        if (denied is not null) return denied;
        try
        {
            var command = RemoveComponentStockCommandAssembler.ToCommandFromResource(technicianId, componentId);
            var result = await inventoryCommandService.Handle(command);
            if (!result) return NotFound();

            return NoContent();
        }
        catch (KeyNotFoundException) { return PropertyOwnerRoute.NotFound(this); }
        catch (AssetNotFoundException) { return PropertyOwnerRoute.NotFound(this); }
        // Domain rule: a component with remaining stock cannot be removed.
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Get technician inventory by technician ID", OperationId = "GetInventoryByTechnicianId")]
    [SwaggerResponse(StatusCodes.Status200OK, "Inventory found", typeof(TechnicianInventoryResource))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Inventory not found")]
    public async Task<IActionResult> GetInventoryByTechnicianId([FromRoute] string technicianId)
    {
        var (technician, denied) = await TechnicianRoute.AuthorizeAsync(this, profiles, technicianId);
        if (denied is not null) return denied;
        var query = new GetInventoryByTechnicianIdQuery(technician!);
        var readModel = await inventoryQueryService.Handle(query);
        if (readModel is null) return NotFound();

        var response = await BuildResponseAsync(technicianId);
        return Ok(response);
    }

    [HttpPatch("{componentId}/increase")]
    [SwaggerOperation(Summary = "Increase stock for a component in inventory", OperationId = "IncreaseComponentStock")]
    [SwaggerResponse(StatusCodes.Status200OK, "Stock increased", typeof(TechnicianInventoryResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Invalid amount")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Inventory or component not found")]
    public async Task<IActionResult> IncreaseComponentStock(
        [FromRoute] string technicianId,
        [FromRoute] string componentId,
        [FromBody] AdjustStockAmountResource resource)
    {
        var (technician, denied) = await TechnicianRoute.AuthorizeAsync(this, profiles, technicianId);
        if (denied is not null) return denied;
        try
        {
            var command = IncreaseStockCommandFromResourceAssembler.ToCommandFromResource(resource, technicianId, componentId);
            var inventory = await inventoryCommandService.Handle(command);
            if (inventory is null) return NotFound();
            var response = await BuildResponseAsync(technicianId);
            return Ok(response);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (AssetNotFoundException) { return PropertyOwnerRoute.NotFound(this); }
    }

    [HttpPatch("{componentId}/decrease")]
    [SwaggerOperation(Summary = "Decrease stock for a component in inventory", OperationId = "DecreaseComponentStock")]
    [SwaggerResponse(StatusCodes.Status200OK, "Stock decreased", typeof(TechnicianInventoryResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Invalid amount or insufficient stock")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Inventory or component not found")]
    public async Task<IActionResult> DecreaseComponentStock(
        [FromRoute] string technicianId,
        [FromRoute] string componentId,
        [FromBody] AdjustStockAmountResource resource)
    {
        var (technician, denied) = await TechnicianRoute.AuthorizeAsync(this, profiles, technicianId);
        if (denied is not null) return denied;
        try
        {
            var command = DecreaseStockCommandFromResourceAssembler.ToCommandFromResource(resource, technicianId, componentId);
            var inventory = await inventoryCommandService.Handle(command);
            if (inventory is null) return NotFound();
            var response = await BuildResponseAsync(technicianId);
            return Ok(response);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (AssetNotFoundException) { return PropertyOwnerRoute.NotFound(this); }
    }

    [HttpGet("stock-items")]
    [SwaggerOperation(Summary = "Get Technician Inventory Stock Items", OperationId = "GetTechnicianInventoryStockItems")]
    [SwaggerResponse(StatusCodes.Status200OK, "The list of stock items with component details", typeof(IEnumerable<ComponentStockResource>))]
    public async Task<IActionResult> GetInventoryItems([FromRoute] string technicianId)
    {
        var (technician, denied) = await TechnicianRoute.AuthorizeAsync(this, profiles, technicianId);
        if (denied is not null) return denied;
        var query = new GetStockItemsByTechnicianIdQuery(technician!);
        var stockItems = await inventoryQueryService.Handle(query);
        
        var resources = stockItems.Select(item => new ComponentStockResource(
            item.StockId,
            item.ComponentId,
            item.ComponentTypeId,
            item.ComponentName,
            item.QuantityAvailable,
            item.AlertThreshold,
            item.LastUpdated
        ));

        return Ok(resources);
    }
    
    private async Task<TechnicianInventoryResource> BuildResponseAsync(string technicianId)
    {
        var query = new GetInventoryByTechnicianIdQuery(TechnicianId.From(technicianId));
        var readModel = await inventoryQueryService.Handle(query);
        if (readModel is not null)
            return TechnicianInventoryResourceFromEntityAssembler.ToResourceFromReadModel(readModel);

        // Fallback vacío si el read model no está disponible
        return new TechnicianInventoryResource(technicianId, []);
    }
}
