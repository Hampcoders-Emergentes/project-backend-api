using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Profiles.Domain.Services;
using Hampcoders.Electrolink.API.Profiles.Interfaces.REST.Resources;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Hampcoders.Electrolink.API.Profiles.Interfaces.REST;

[Route("api/v1/profiles/me/device-onboarding")]
[ApiController]
[Produces("application/json")]
[SwaggerTag("Device Onboarding endpoints")]
public class DeviceOnboardingController(
    IDeviceOnboardingService onboardingService)
    : ControllerBase
{
    private static DeviceOnboardingAnalysisResource ToResource(DeviceOnboardingAnalysis analysis)
    {
        return new DeviceOnboardingAnalysisResource(
            analysis.Reasoning,
            analysis.Narrative,
            new SuggestedThresholdsResource(
                analysis.SuggestedThresholds.NominalVoltage,
                analysis.SuggestedThresholds.MaxConsumptionWatts,
                analysis.SuggestedThresholds.MaxCurrentAmps,
                analysis.SuggestedThresholds.MinPowerFactor,
                analysis.SuggestedThresholds.NominalFrequency,
                analysis.SuggestedThresholds.DisconnectionThresholdMin
            ),
            analysis.AnalysisId
        );
    }

    [HttpPost("analyze")]
    [SwaggerOperation(Summary = "Analyze device and suggest thresholds", OperationId = "AnalyzeDeviceOnboarding")]
    [ProducesResponseType(typeof(DeviceOnboardingAnalysisResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Analyze(
        [FromBody] AnalyzeDeviceOnboardingResource resource)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();

        var request = new AnalyzeDeviceOnboardingRequest(
            resource.DeviceType,
            resource.NominalVoltage,
            resource.DailyUsageHours,
            resource.Occupants,
            resource.LocationType,
            resource.PrimaryUse
        );

        var analysis = await onboardingService.AnalyzeAsync(actor.Id.Value, request);
        return Ok(ToResource(analysis));
    }

    [HttpPost("adjust")]
    [SwaggerOperation(Summary = "Adjust thresholds based on user feedback", OperationId = "AdjustDeviceOnboarding")]
    [ProducesResponseType(typeof(DeviceOnboardingAnalysisResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Adjust(
        [FromBody] AdjustThresholdsResource resource)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();

        try
        {
            var analysis = await onboardingService.AdjustAnalysisAsync(
                actor.Id.Value,
                resource.AnalysisId,
                resource.UserMessage
            );
            return Ok(ToResource(analysis));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
