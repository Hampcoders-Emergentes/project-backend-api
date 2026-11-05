using System.Net.Mime;
using Hampcoders.Electrolink.API.Analytics.Domain.Services;
using Hampcoders.Electrolink.API.Analytics.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Analytics.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Assets.Interfaces.ACL;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Interfaces.REST;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.ACL;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Hampcoders.Electrolink.API.Analytics.Interfaces.REST;

[ApiController]
[Route("api/v1/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Consumption Report endpoints")]
public class ConsumptionReportController(
    IConsumptionReportQueryService queryService,
    IConsumptionReportCommandService commandService,
    ISubscriptionContextFacade subscriptionFacade,
    IProfilesContextFacade profiles,
    IAssetsContextFacade assets,
    ILogger<ConsumptionReportController> logger)
    : ControllerBase
{
    [HttpGet("by-client/{homeownerId}")]
    [SwaggerOperation(
        Summary = "Get reports by homeowner",
        Description = "Returns all reports generated for the given homeowner.",
        OperationId = "GetReportsByHomeowner")]
    [SwaggerResponse(StatusCodes.Status200OK, "Reports found", typeof(IEnumerable<ConsumptionReportResource>))]
    public async Task<IActionResult> GetReportsByHomeowner(string homeownerId)
    {
        var denied = await this.DenyUnlessClientAsync(profiles, homeownerId);
        if (denied is not null) return denied;

        var reports = await queryService.GetReportsByHomeownerAsync(homeownerId);
        var resources = reports.Select(
            ConsumptionReportResourceFromEntityAssembler.ToResourceFromEntity);
        return Ok(resources);
    }

    [HttpPost("request")]
    [SwaggerOperation(
        Summary = "Request a consumption report",
        Description = "Generates a new consumption report for the given period and format.",
        OperationId = "RequestConsumptionReport")]
    [SwaggerResponse(StatusCodes.Status200OK, "Report requested", typeof(string))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Invalid request")]
    public async Task<IActionResult> RequestReport(
        [FromBody] RequestConsumptionReportResource resource)
    {
        var denied = await this.DenyUnlessClientAsync(profiles, resource.OwnerId);
        if (denied is not null) return denied;

        try
        {
            if (!await assets.PropertyBelongsToOwnerAsync(resource.PropertyId, resource.OwnerId))
                return this.ResourceNotFound();

            var eligibility = await subscriptionFacade.GetRequestEligibilityAsync(resource.OwnerId);
            var planTier = eligibility.planType;

            var reportId = await commandService.RequestConsumptionReportAsync(
                resource.OwnerId,
                resource.PropertyId,
                resource.PeriodStart,
                resource.PeriodEnd,
                planTier,
                resource.ExportFormat);

            return Ok(new { reportId });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
