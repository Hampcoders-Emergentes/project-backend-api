using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hampcoders.Electrolink.API.AlertManagement.Application;
using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Auditing;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Microsoft.AspNetCore.Mvc;
using HumanAuthorize = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AuthorizeAttribute;
namespace Hampcoders.Electrolink.API.AlertManagement.Interfaces;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssignAlertResource([Required, StringLength(100)] string TechnicianId, [StringLength(120)] string? TechnicianName);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResolveAlertResource([StringLength(1000)] string? Notes);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReportEquipmentAnomalyResource(
    [Required, StringLength(30)] string Category,
    [Required, StringLength(1000)] string Description,
    [StringLength(60)] string? ReportedByWorkerCode,
    [StringLength(120)] string? ReportedByWorkerName);

/// <summary>Incident workflow: technician assignment and resolution of alerts of the actor's stores.</summary>
[ApiController, HumanAuthorize]
[Route("api/v1/alerts/{alertId}")]
[Produces("application/json")]
public sealed class AlertWorkflowController(AlertService alerts) : ControllerBase
{
    /// <summary>Assigns an unresolved alert to a technician; the alert becomes ASSIGNED.</summary>
    [HttpPost("assign")]
    public Task<ActionResult<AlertResource>> Assign(string alertId, AssignAlertResource body) =>
        Execute(actor => alerts.Handle(new AssignAlertCommand(actor.Id, alertId, body.TechnicianId, body.TechnicianName), HttpContext.RequestAborted));

    /// <summary>Closes the incident; the alert becomes RESOLVED.</summary>
    [HttpPost("resolve")]
    public Task<ActionResult<AlertResource>> Resolve(string alertId, ResolveAlertResource? body) =>
        Execute(actor => alerts.Handle(new ResolveAlertCommand(actor.Id, alertId, body?.Notes), HttpContext.RequestAborted));

    private async Task<ActionResult<AlertResource>> Execute(Func<User, Task<AlertTransitionResult>> action)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try
        {
            var result = await action(actor);
            // The route carries no store, so the audit trail is told which store the event belongs to.
            AuditContext.SetStore(HttpContext, result.StoreId);
            return Ok(result.Alert);
        }
        catch (HierarchyNotFoundException) { return NotFound(new { message = "Resource not found." }); }
        catch (AlertStateException e) { return Conflict(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
}

/// <summary>Manual anomaly reports made by kitchen staff (abnormal noise, sparks, burning smell...).</summary>
[ApiController, HumanAuthorize]
[Route("api/v1/stores/{storeId}/areas/{areaId}/equipment/{equipmentId}/anomalies")]
[Produces("application/json")]
public sealed class EquipmentAnomalyReportsController(AlertService alerts) : ControllerBase
{
    /// <summary>
    ///     Reports an anomaly seen or heard on the equipment. Creates a NON_CRITICAL alert of type MANUAL_REPORT attributed to
    ///     the reporting worker (defaults to the worker on shift). Categories: ABNORMAL_NOISE, SPARKS, BURNING_SMELL,
    ///     OVERHEATING, FLICKERING_LIGHTS, SMOKE, OTHER.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(AlertResource), StatusCodes.Status201Created)]
    public async Task<ActionResult<AlertResource>> Report(string storeId, string areaId, string equipmentId, ReportEquipmentAnomalyResource body)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try
        {
            var alert = await alerts.Handle(new ReportEquipmentAnomalyCommand(actor.Id, storeId, areaId, equipmentId, body.Category,
                body.Description, body.ReportedByWorkerCode, body.ReportedByWorkerName), HttpContext.RequestAborted);
            return Created($"/api/v1/stores/{storeId}/areas/{areaId}/equipment/{equipmentId}/alerts/{alert.Id}", alert);
        }
        catch (HierarchyNotFoundException) { return NotFound(new { message = "Resource not found." }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
}
