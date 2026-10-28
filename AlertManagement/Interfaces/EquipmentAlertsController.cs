using Hampcoders.Electrolink.API.AlertManagement.Application;
using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Microsoft.AspNetCore.Mvc;
using HumanAuthorize = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AuthorizeAttribute;
namespace Hampcoders.Electrolink.API.AlertManagement.Interfaces;

[ApiController, HumanAuthorize]
[Route("api/v1/stores/{storeId}/areas/{areaId}/equipment/{equipmentId}/alerts")]
public sealed class EquipmentAlertsController(AlertService alerts) : ControllerBase
{
    [HttpGet]
    public Task<ActionResult<HierarchyPage<AlertResource>>> List(string storeId, string areaId, string equipmentId,
        [FromQuery] string? status = null, [FromQuery] string? severity = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? from = null, [FromQuery] string? to = null) =>
        Execute(storeId, areaId, equipmentId, scope => alerts.List(scope, status, severity, page, pageSize, HttpContext.RequestAborted, from, to));
    [HttpGet("{alertId}")]
    public Task<ActionResult<AlertResource>> Get(string storeId, string areaId, string equipmentId, string alertId) =>
        Execute(storeId, areaId, equipmentId, scope => alerts.Get(scope, alertId, HttpContext.RequestAborted));
    [HttpPost("{alertId}/acknowledgement")]
    public Task<ActionResult<AlertResource>> Acknowledge(string storeId, string areaId, string equipmentId, string alertId) =>
        Execute(storeId, areaId, equipmentId, scope => alerts.Acknowledge(scope, alertId, HttpContext.RequestAborted));
    private async Task<ActionResult<T>> Execute<T>(string storeId, string areaId, string equipmentId, Func<AlertScope, Task<T>> action)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try { return Ok(await action(new(actor.Id, storeId, areaId, equipmentId))); }
        catch (HierarchyNotFoundException) { return NotFound(new { message = "Resource not found." }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
}
