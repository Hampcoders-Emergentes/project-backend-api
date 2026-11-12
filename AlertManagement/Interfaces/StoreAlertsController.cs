using Hampcoders.Electrolink.API.Shared.Interfaces.REST;
using Hampcoders.Electrolink.API.AlertManagement.Application;
using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Microsoft.AspNetCore.Mvc;
using HumanAuthorize = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AuthorizeAttribute;
namespace Hampcoders.Electrolink.API.AlertManagement.Interfaces;

[ApiController, HumanAuthorize]
[Route("api/v1/stores/{storeId}/alerts")]
[Produces("application/json")]
public sealed class StoreAlertsController(AlertService alerts) : ControllerBase
{
    /// <summary>Paged alert history. Filters: status=OPEN|ACKNOWLEDGED, severity=CRITICAL|NON_CRITICAL, equipmentId, from/to.</summary>
    [HttpGet]
    public Task<ActionResult<HierarchyPage<AlertResource>>> List(string storeId,
        [FromQuery] string? equipmentId = null, [FromQuery] string? status = null, [FromQuery] string? severity = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? from = null, [FromQuery] string? to = null) =>
        Execute(scope => alerts.ListStore(scope, status, severity, page, pageSize, HttpContext.RequestAborted, from, to), storeId, equipmentId);

    /// <summary>Open operational emergencies, critical first (at most 100), with store-wide open counters.</summary>
    [HttpGet("active")]
    public Task<ActionResult<ActiveAlertsResource>> Active(string storeId, [FromQuery] string? equipmentId = null) =>
        Execute(scope => alerts.ListActive(scope, HttpContext.RequestAborted), storeId, equipmentId);

    /// <summary>
    ///     Delta for reconnecting realtime clients: alerts created or changed after <paramref name="afterSequence"/> (current
    ///     state, ordered by sequence) and the cursor for the next call. Start from the latestSequence of GET .../alerts/active
    ///     or from the highest sequence received over the hub.
    /// </summary>
    [HttpGet("changes")]
    [ProducesResponseType(typeof(AlertChangesResponse), StatusCodes.Status200OK)]
    public Task<ActionResult<AlertChangesResponse>> Changes(string storeId, [FromQuery] long afterSequence = 0,
        [FromQuery] int limit = 100, [FromQuery] string? equipmentId = null) =>
        Execute(scope => alerts.Changes(scope, afterSequence, limit, HttpContext.RequestAborted), storeId, equipmentId);

    private async Task<ActionResult<T>> Execute<T>(Func<StoreAlertScope, Task<T>> action, string storeId, string? equipmentId)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        // MVC maps empty strings to null. Preserve presence so an empty equipment filter is rejected.
        var equipment = Request.Query.ContainsKey("equipmentId") ? equipmentId ?? "" : null;
        try { return Ok(await action(new StoreAlertScope(actor.Id, storeId, equipment))); }
        catch (HierarchyNotFoundException notFound) { return NotFound(ErrorBodies.Coded("Resource not found.", notFound.ErrorCode)); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
}
