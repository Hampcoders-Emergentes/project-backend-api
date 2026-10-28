using System.Security.Claims;
using Hampcoders.Electrolink.API.Processing.Infrastructure.Pipeline.Middleware.Attributes;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Processing.Domain.Services;
using Hampcoders.Electrolink.API.Processing.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Processing.Interfaces.REST.Transform;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Hampcoders.Electrolink.API.Processing.Interfaces.REST;

[ApiController]
[Route("api/v1/iot/devices")]
[Produces("application/json")]
public class DeviceMonitoringController(
    IIoTMonitoringQueryService queryService,
    IAnomalyCommandService     anomalyService) : ControllerBase
{
    [HttpGet("{deviceId}/status")]
    [SwaggerOperation(Summary = "Get device status", OperationId = "GetDeviceStatus")]
    [ProducesResponseType(typeof(DeviceStatusResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeviceStatusResource>> GetDeviceStatus(string deviceId)
    {
        var stream = await queryService.Handle(new GetDeviceStatusQuery(deviceId));
        if (stream is null) return NotFound(new { message = $"Device {deviceId} not found." });

        var anomalies    = await queryService.Handle(new GetActiveAnomaliesByPropertyQuery(stream.PropertyId.Value));
        var pendingRelay = await queryService.FindPendingRelayCommandByDeviceAsync(deviceId);
        var relayState   = pendingRelay?.TargetRelayState.ToString() ?? "UNKNOWN";

        return Ok(DeviceStatusResourceFromEntityAssembler.ToResourceFromEntity(stream, anomalies, relayState));
    }

    [HttpGet("{deviceId}/readings/recent")]
    [SwaggerOperation(Summary = "Get recent readings in time window", OperationId = "GetRecentReadings")]
    [ProducesResponseType(typeof(RecentReadingsResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecentReadingsResource>> GetRecentReadings(
        string deviceId, [FromQuery] int windowMinutes = 60)
    {
        var stream = await queryService.Handle(new GetRecentReadingsWindowQuery(deviceId, windowMinutes));
        if (stream is null) return NotFound(new { message = $"Device {deviceId} not found." });

        var since   = DateTime.UtcNow.AddMinutes(-windowMinutes);
        var window  = stream.Readings
            .Where(r => r.Timestamp >= since)
            .OrderByDescending(r => r.Timestamp)
            .ToList();

        return Ok(new RecentReadingsResource(
            stream.DeviceId.Value,
            stream.PropertyId.Value,
            windowMinutes,
            window.Select(r => new ReadingValueResource(
                r.Voltage, r.Current, r.PowerFactor, r.Frequency, r.Timestamp)).ToList(),
            window.Count > 0 ? window.Average(r => r.ActivePowerKw) : 0f,
            window.Count > 0 ? window.Max(r => r.Current) : 0f));
    }

    [HttpGet("properties/{propertyId}/anomalies")]
    [SwaggerOperation(Summary = "Get active anomalies by property", OperationId = "GetActiveAnomalies")]
    [ProducesResponseType(typeof(IEnumerable<AnomalyResource>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<AnomalyResource>>> GetActiveAnomalies(string propertyId)
    {
        var anomalies = await queryService.Handle(new GetActiveAnomaliesByPropertyQuery(propertyId));
        return Ok(anomalies.Select(AnomalyResourceFromEntityAssembler.ToResourceFromEntity));
    }

    [HttpGet("{deviceId}/relay/history")]
    [AllowEdgeApiKey]
    [RequireRelayHistoryDeviceAccess]
    [SwaggerOperation(Summary = "Get relay command history for device", OperationId = "GetRelayHistory")]
    [ProducesResponseType(typeof(RelayCommandHistoryResource), StatusCodes.Status200OK)]
    public async Task<ActionResult<RelayCommandHistoryResource>> GetRelayHistory(string deviceId)
    {
        var commands = await queryService.Handle(new GetRelayCommandHistoryQuery(deviceId));
        return Ok(RelayHistoryResourceFromEntityAssembler.ToResourceFromEntities(deviceId, commands));
    }

    [HttpPost("anomalies/{anomalyId}/acknowledge")]
    [SwaggerOperation(Summary = "Acknowledge an anomaly manually", OperationId = "AcknowledgeAnomaly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AcknowledgeAnomaly(string anomalyId)
    {
        try
        {
            var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? throw new UnauthorizedAccessException("Actor identity not found.");

            await anomalyService.Handle(new AcknowledgeAnomalyCommand(anomalyId, actorId));
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpPost("anomalies/{anomalyId}/force-resolve")]
    [SwaggerOperation(Summary = "Force resolve a CRITICAL anomaly", OperationId = "ForceResolveAnomaly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForceResolveAnomaly(string anomalyId)
    {
        try
        {
            var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? throw new UnauthorizedAccessException("Actor identity not found.");

            await anomalyService.Handle(new ForceResolveAnomalyCommand(anomalyId, actorId));
            return NoContent();
        }
        catch (KeyNotFoundException ex)      { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
