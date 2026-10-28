using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Microsoft.AspNetCore.Mvc;
using HumanAuthorize = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AuthorizeAttribute;
namespace Hampcoders.Electrolink.API.StoreManagement.Interfaces.REST;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LinkEquipmentDeviceResource([Required, StringLength(100)] string DeviceId,
    [Required, StringLength(200)] string EdgeDeviceId);

[ApiController, HumanAuthorize]
[Route("api/v1/stores/{storeId}/areas/{areaId}/equipment/{equipmentId}")]
public sealed class EquipmentDeviceController(EquipmentTelemetryService telemetry) : ControllerBase
{
    [HttpPost("device-binding")]
    public Task<ActionResult<DeviceBindingResource>> Link(string storeId, string areaId, string equipmentId, LinkEquipmentDeviceResource body) =>
        Execute(actor => telemetry.Link(actor, storeId, areaId, equipmentId, body.DeviceId, body.EdgeDeviceId, HttpContext.RequestAborted),
            value => CreatedAtAction(nameof(GetBinding), new { storeId, areaId, equipmentId }, value));
    [HttpGet("device-binding")]
    public Task<ActionResult<DeviceBindingResource>> GetBinding(string storeId, string areaId, string equipmentId) =>
        Execute(actor => telemetry.GetBinding(actor, storeId, areaId, equipmentId, HttpContext.RequestAborted));
    [HttpGet("telemetry")]
    public Task<ActionResult<EquipmentTelemetryResource>> GetState(string storeId, string areaId, string equipmentId) =>
        Execute(actor => telemetry.GetState(actor, storeId, areaId, equipmentId, HttpContext.RequestAborted));
    /// <summary>Stored samples in [from, to), ordered by capture then reading ID. Explicit offsets required.</summary>
    [HttpGet("measurements")]
    public Task<ActionResult<HierarchyPage<MeasurementHistoryResource>>> GetHistory(string storeId, string areaId, string equipmentId,
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        Execute(actor => telemetry.GetHistory(actor, storeId, areaId, equipmentId,
            MeasurementHistoryQuery.Parse(from, to, page, pageSize), HttpContext.RequestAborted));
    private async Task<ActionResult<T>> Execute<T>(Func<UserId, Task<T>> action, Func<T, ActionResult<T>>? created = null)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try { var value = await action(actor.Id); return created != null ? created(value) : Ok(value); }
        catch (HierarchyNotFoundException) { return NotFound(new { message = "Resource not found." }); }
        catch (EquipmentAccessException e) { return StatusCode(403, new { message = e.Message }); }
        catch (EquipmentConflictException e) { return Conflict(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
}
