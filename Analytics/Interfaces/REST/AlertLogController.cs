using System.Net.Mime;
using Hampcoders.Electrolink.API.Analytics.Domain.Services;
using Hampcoders.Electrolink.API.Analytics.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Analytics.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Interfaces.REST;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Hampcoders.Electrolink.API.Analytics.Interfaces.REST;

[ApiController]
[Route("api/v1/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Alert Log endpoints")]
public class AlertLogController(
    IAlertLogQueryService queryService,
    IAlertLogCommandService commandService,
    IProfilesContextFacade profiles,
    ILogger<AlertLogController> logger)
    : ControllerBase
{
    [HttpGet("{homeownerId}")]
    [SwaggerOperation(
        Summary = "Get alert history for a homeowner",
        Description = "Returns the full alert log for the given homeowner.",
        OperationId = "GetAlertHistory")]
    [SwaggerResponse(StatusCodes.Status200OK, "Alert log found", typeof(AlertLogResource))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Alert log not found")]
    public async Task<IActionResult> GetAlertHistory(string homeownerId)
    {
        var denied = await this.DenyUnlessClientAsync(profiles, homeownerId);
        if (denied is not null) return denied;

        var log = await queryService.GetAlertHistoryAsync(homeownerId);
        if (log == null)
            return NotFound(new { message = "No alert log found for the given homeowner." });

        var resource = AlertLogResourceFromEntityAssembler.ToResourceFromEntity(log);
        return Ok(resource);
    }

    [HttpPut("{homeownerId}/acknowledge")]
    [SwaggerOperation(
        Summary = "Acknowledge an alert",
        Description = "Marks a specific alert entry as acknowledged.",
        OperationId = "AcknowledgeAlert")]
    [SwaggerResponse(StatusCodes.Status200OK, "Alert acknowledged")]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Invalid request")]
    public async Task<IActionResult> AcknowledgeAlert(
        string homeownerId, [FromBody] AcknowledgeAlertResource resource)
    {
        var denied = await this.DenyUnlessClientAsync(profiles, homeownerId);
        if (denied is not null) return denied;

        try
        {
            await commandService.AcknowledgeAlertAsync(homeownerId, resource.EntryId);
            return Ok(new { message = "Alert acknowledged successfully." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{homeownerId}/link-to-service")]
    [SwaggerOperation(
        Summary = "Link alert to service request",
        Description = "Links an alert entry to a service request.",
        OperationId = "LinkAlertToService")]
    [SwaggerResponse(StatusCodes.Status200OK, "Alert linked")]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Invalid request")]
    public async Task<IActionResult> LinkAlertToService(
        string homeownerId, [FromBody] LinkAlertToServiceResource resource)
    {
        var denied = await this.DenyUnlessClientAsync(profiles, homeownerId);
        if (denied is not null) return denied;

        try
        {
            await commandService.LinkAlertToServiceRequestAsync(
                homeownerId, resource.EntryId, resource.ServiceRequestId);
            return Ok(new { message = "Alert linked to service request successfully." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
