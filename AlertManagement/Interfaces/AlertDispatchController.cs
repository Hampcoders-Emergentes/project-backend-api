using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hampcoders.Electrolink.API.AlertManagement.Application;
using Hampcoders.Electrolink.API.Processing.Infrastructure.Pipeline.Middleware.Attributes;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Microsoft.AspNetCore.Mvc;
namespace Hampcoders.Electrolink.API.AlertManagement.Interfaces;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DispatchAlertResource([Required, StringLength(70)] string AlertId);

/// <summary>
///     Internal dispatch trigger for the ingestion pipeline (X-Api-Key). Re-sends a stored alert to in-app clients and
///     queues the external routes the store requires that were not queued yet; safe to call repeatedly.
/// </summary>
[ApiController]
[Route("api/v1/alerts/dispatch")]
[Produces("application/json")]
[RequireEdgeApiKey]
public sealed class AlertDispatchController(AlertService alerts) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(AlertDispatchResultResource), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Dispatch(DispatchAlertResource body)
    {
        try { return Accepted(await alerts.Dispatch(body.AlertId, HttpContext.RequestAborted)); }
        catch (HierarchyNotFoundException) { return NotFound(new { message = "Alert not found." }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
}
