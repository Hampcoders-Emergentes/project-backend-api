using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Microsoft.AspNetCore.Mvc;
using HumanAuthorize = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AuthorizeAttribute;

namespace Hampcoders.Electrolink.API.StoreManagement.Interfaces.REST;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateStoreResource(
    [Required, StringLength(120)] string Name,
    [Required, StringLength(300)] string Address);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateNamedHierarchyResource([Required, StringLength(120)] string Name);

[ApiController]
[Route("api/v1/stores")]
[Produces("application/json")]
[HumanAuthorize]
public sealed class StoresController(StoreHierarchyService service) : ControllerBase
{
    [HttpPost]
    public Task<ActionResult<StoreResource>> CreateStore(CreateStoreResource body) => Execute(
        actor => service.CreateStore(actor, body.Name, body.Address, HttpContext.RequestAborted),
        item => CreatedAtAction(nameof(GetStore), new { storeId = item.Id }, item));
    [HttpGet]
    public Task<ActionResult<HierarchyPage<StoreResource>>> ListStores([FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        Execute(actor => service.ListStores(actor, page, pageSize, HttpContext.RequestAborted));
    [HttpGet("{storeId}")]
    public Task<ActionResult<StoreResource>> GetStore(string storeId) =>
        Execute(actor => service.GetStore(actor, storeId, HttpContext.RequestAborted));
    [HttpPost("{storeId}/areas")]
    public Task<ActionResult<AreaResource>> CreateArea(string storeId, CreateNamedHierarchyResource body) => Execute(
        actor => service.CreateArea(actor, storeId, body.Name, HttpContext.RequestAborted),
        item => CreatedAtAction(nameof(GetArea), new { storeId, areaId = item.Id }, item));
    [HttpGet("{storeId}/areas")]
    public Task<ActionResult<HierarchyPage<AreaResource>>> ListAreas(string storeId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        Execute(actor => service.ListAreas(actor, storeId, page, pageSize, HttpContext.RequestAborted));
    [HttpGet("{storeId}/areas/{areaId}")]
    public Task<ActionResult<AreaResource>> GetArea(string storeId, string areaId) =>
        Execute(actor => service.GetArea(actor, storeId, areaId, HttpContext.RequestAborted));
    [HttpPost("{storeId}/areas/{areaId}/equipment")]
    public Task<ActionResult<EquipmentResource>> CreateEquipment(string storeId, string areaId, CreateNamedHierarchyResource body) => Execute(
        actor => service.CreateEquipment(actor, storeId, areaId, body.Name, HttpContext.RequestAborted),
        item => CreatedAtAction(nameof(GetEquipment), new { storeId, areaId, equipmentId = item.Id }, item));
    [HttpGet("{storeId}/areas/{areaId}/equipment")]
    public Task<ActionResult<HierarchyPage<EquipmentResource>>> ListEquipment(string storeId, string areaId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        Execute(actor => service.ListEquipment(actor, storeId, areaId, page, pageSize, HttpContext.RequestAborted));
    [HttpGet("{storeId}/areas/{areaId}/equipment/{equipmentId}")]
    public Task<ActionResult<EquipmentResource>> GetEquipment(string storeId, string areaId, string equipmentId) =>
        Execute(actor => service.GetEquipment(actor, storeId, areaId, equipmentId, HttpContext.RequestAborted));

    private async Task<ActionResult<T>> Execute<T>(Func<UserId, Task<T>> action, Func<T, ActionResult<T>>? created = null)
    {
        // IAM middleware has resolved the token to a real account. Never use a request owner/profile ID.
        if (HttpContext.Items["User"] is not User user) return Unauthorized();
        try
        {
            var result = await action(user.Id);
            return created != null ? created(result) : Ok(result);
        }
        catch (HierarchyNotFoundException) { return NotFound(new { message = "Resource not found." }); }
        catch (ArgumentException error) { return BadRequest(new { message = error.Message }); }
    }
}
