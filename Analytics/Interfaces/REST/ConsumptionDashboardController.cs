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
[SwaggerTag("Consumption Dashboard endpoints")]
public class ConsumptionDashboardController(
    IConsumptionDashboardQueryService queryService,
    IConsumptionDashboardCommandService commandService,
    IProfilesContextFacade profiles,
    ILogger<ConsumptionDashboardController> logger)
    : ControllerBase
{
    [HttpGet("{homeownerId}")]
    [SwaggerOperation(
        Summary = "Get dashboard view for a homeowner",
        Description = "Returns the real-time consumption dashboard for the given homeowner.",
        OperationId = "GetDashboardView")]
    [SwaggerResponse(StatusCodes.Status200OK, "Dashboard found", typeof(ConsumptionDashboardResource))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Dashboard not found")]
    public async Task<IActionResult> GetDashboardView(string homeownerId)
    {
        var denied = await this.DenyUnlessClientAsync(profiles, homeownerId);
        if (denied is not null) return denied;

        var dashboard = await queryService.GetDashboardViewAsync(homeownerId);
        if (dashboard == null)
            return NotFound(new { message = "Dashboard not found for the given homeowner." });

        var resource = ConsumptionDashboardResourceFromEntityAssembler.ToResourceFromEntity(dashboard);
        return Ok(resource);
    }

    [HttpGet("{homeownerId}/cost-projection")]
    [SwaggerOperation(
        Summary = "Get cost projection for a homeowner",
        Description = "Returns the projected monthly cost based on current consumption.",
        OperationId = "GetCostProjection")]
    [SwaggerResponse(StatusCodes.Status200OK, "Cost projection found", typeof(ConsumptionDashboardResource))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Cost projection not available")]
    public async Task<IActionResult> GetCostProjection(string homeownerId)
    {
        var denied = await this.DenyUnlessClientAsync(profiles, homeownerId);
        if (denied is not null) return denied;

        var dashboard = await queryService.GetCostProjectionAsync(homeownerId);
        if (dashboard == null)
            return NotFound(new { message = "Cost projection not available for the given homeowner." });

        var resource = ConsumptionDashboardResourceFromEntityAssembler.ToResourceFromEntity(dashboard);
        return Ok(resource);
    }

    [HttpPut("{homeownerId}/upgrade-tier")]
    [SwaggerOperation(
        Summary = "Upgrade dashboard tier",
        Description = "Upgrades the plan tier for the given homeowner's dashboard.",
        OperationId = "UpgradeDashboardTier")]
    [SwaggerResponse(StatusCodes.Status200OK, "Tier upgraded")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Dashboard not found")]
    public async Task<IActionResult> UpgradeTier(string homeownerId, [FromBody] UpgradeTierResource resource)
    {
        var denied = await this.DenyUnlessClientAsync(profiles, homeownerId);
        if (denied is not null) return denied;

        try
        {
            await commandService.UpgradeDashboardTierAsync(homeownerId, resource.NewPlanTier);
            return Ok(new { message = "Dashboard tier upgraded successfully." });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPut("{homeownerId}/thresholds")]
    [SwaggerOperation(
        Summary = "Update consumption thresholds",
        Description = "Updates the consumption thresholds for the given homeowner's dashboard.",
        OperationId = "UpdateConsumptionThresholds")]
    [SwaggerResponse(StatusCodes.Status200OK, "Thresholds updated")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Dashboard not found")]
    public async Task<IActionResult> UpdateThresholds(
        string homeownerId, [FromBody] Dictionary<string, decimal> thresholds)
    {
        var denied = await this.DenyUnlessClientAsync(profiles, homeownerId);
        if (denied is not null) return denied;

        try
        {
            await commandService.UpdateConsumptionThresholdsAsync(homeownerId, thresholds);
            return Ok(new { message = "Consumption thresholds updated successfully." });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
