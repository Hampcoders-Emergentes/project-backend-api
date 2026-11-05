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
[SwaggerTag("Technician Metrics endpoints")]
public class TechnicianMetricsController(
    ITechnicianMetricsQueryService queryService,
    ITechnicianMetricsCommandService commandService,
    IProfilesContextFacade profiles,
    ILogger<TechnicianMetricsController> logger)
    : ControllerBase
{
    [HttpGet("{technicianId}")]
    [SwaggerOperation(
        Summary = "Get performance dashboard for a technician",
        Description = "Returns the current period metrics for the given technician.",
        OperationId = "GetTechnicianPerformance")]
    [SwaggerResponse(StatusCodes.Status200OK, "Metrics found", typeof(TechnicianMetricsResource))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Metrics not found")]
    public async Task<IActionResult> GetPerformanceDashboard(string technicianId)
    {
        var denied = await this.DenyUnlessTechnicianAsync(profiles, technicianId);
        if (denied is not null) return denied;

        var metrics = await queryService.GetPerformanceDashboardAsync(technicianId);
        if (metrics == null)
            return NotFound(new { message = "No metrics found for the given technician." });

        var resource = TechnicianMetricsResourceFromEntityAssembler.ToResourceFromEntity(metrics);
        return Ok(resource);
    }

    [HttpPost("{technicianId}/initialize-period")]
    [SwaggerOperation(
        Summary = "Initialize a new metrics period",
        Description = "Creates a new metrics tracking period for the given technician.",
        OperationId = "InitializeTechnicianPeriod")]
    [SwaggerResponse(StatusCodes.Status200OK, "Period initialized")]
    public async Task<IActionResult> InitializePeriod(string technicianId)
    {
        var denied = await this.DenyUnlessTechnicianAsync(profiles, technicianId);
        if (denied is not null) return denied;

        await commandService.InitializeTechnicianMetricsPeriodAsync(technicianId);
        return Ok(new { message = "Technician metrics period initialized." });
    }
}
