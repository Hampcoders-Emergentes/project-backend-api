using Hampcoders.Electrolink.API.AlertManagement.Application;
using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Microsoft.AspNetCore.Mvc;
using HumanAuthorize = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AuthorizeAttribute;
namespace Hampcoders.Electrolink.API.AlertManagement.Interfaces;

[ApiController, HumanAuthorize]
[Route("api/v1/stores/{storeId}/alerts")]
public sealed class StoreAlertsController(AlertService alerts) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<HierarchyPage<AlertResource>>> List(string storeId,
        [FromQuery] string? equipmentId = null, [FromQuery] string? status = null, [FromQuery] string? severity = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? from = null, [FromQuery] string? to = null)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        // MVC maps empty strings to null. Preserve presence so an empty equipment filter is rejected.
        var equipment = Request.Query.ContainsKey("equipmentId") ? equipmentId ?? "" : null;
        try { return Ok(await alerts.ListStore(new StoreAlertScope(actor.Id, storeId, equipment), status, severity,
            page, pageSize, HttpContext.RequestAborted, from, to)); }
        catch (HierarchyNotFoundException) { return NotFound(new { message = "Resource not found." }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
}
