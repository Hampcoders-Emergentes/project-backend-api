using Hampcoders.Electrolink.API.Processing.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Processing.Domain.Repositories;
using Hampcoders.Electrolink.API.Processing.Domain.Services;
using Hampcoders.Electrolink.API.Processing.Infrastructure.Pipeline.Middleware.Attributes;
using Hampcoders.Electrolink.API.Processing.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Microsoft.AspNetCore.Mvc;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Swashbuckle.AspNetCore.Annotations;

namespace Hampcoders.Electrolink.API.Processing.Interfaces.REST;

[ApiController]
[Route("api/v1/iot/edge")]
[Produces("application/json")]
[RequireEdgeApiKey]
public class EdgeIngestionController(
    IDeviceReadingStreamCommandService streamService,
    IRelayCommandService               relayService,
    IDeviceReadingStreamRepository     streamRepository,
    IUnitOfWork                        unitOfWork,
    ILogger<EdgeIngestionController>   logger) : ControllerBase
{
    [HttpPost("readings")]
    [SwaggerOperation(Summary = "Receive a sensor reading from the Edge API", OperationId = "IngestReading")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> IngestReading([FromBody] IngestReadingResource resource, [FromServices] EquipmentTelemetryService equipmentTelemetry)
    {
        try
        {
            if (await equipmentTelemetry.TryIngest(resource, HttpContext.RequestAborted)) return NoContent();
        }
        catch (EquipmentAccessException e) { return StatusCode(403, new { message = e.Message }); }
        catch (EquipmentConflictException e) { return Conflict(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }

        var command = new IngestDeviceReadingCommand(
            resource.DeviceId, resource.ReadingId, resource.Timestamp,
            resource.Voltage, resource.Current, resource.PowerFactor,
            resource.Frequency, resource.Source);

        bool accepted = await streamService.Handle(command);
        return accepted ? NoContent() : Conflict(new { message = "Reading rejected." });
    }

    [HttpPost("anomalies")]
    [SwaggerOperation(Summary = "Report an edge-detected anomaly from firmware", OperationId = "ReportEdgeAnomaly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReportEdgeAnomaly([FromBody] ReportEdgeAnomalyResource resource)
    {
        var command = new ReportEdgeAnomalyCommand(
            resource.DeviceId, resource.AnomalyType,
            resource.TriggerReadingId, resource.EdgeAlertPayloadJson);

        await streamService.Handle(command);
        return NoContent();
    }

    [HttpPost("relay/acknowledge")]
    [SwaggerOperation(Summary = "Acknowledge relay command execution from device", OperationId = "AcknowledgeRelay")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AcknowledgeRelay([FromBody] AcknowledgeRelayResource resource)
    {
        try
        {
            var command = new AcknowledgeRelayExecutionCommand(
                resource.CommandId, resource.DeviceId,
                resource.ExecutedSuccessfully, resource.ExecutedAt);

            await relayService.Handle(command);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpGet("devices/{deviceId}/thresholds")]
    [SwaggerOperation(Summary = "Get device thresholds for Edge sync", OperationId = "GetDeviceThresholds")]
    [ProducesResponseType(typeof(DeviceThresholdResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDeviceThresholds(string deviceId, [FromServices] EquipmentTelemetryService equipmentTelemetry)
    {
        try
        {
            var linked = await equipmentTelemetry.TryThresholds(deviceId, HttpContext.RequestAborted);
            if (linked != null) return Ok(linked);
        }
        catch (EquipmentAccessException e) { return StatusCode(403, new { message = e.Message }); }

        var stream = await streamRepository.FindByDeviceIdAsync(deviceId);

        // Auto-create a demo stream if one doesn't exist (Expo / Wokwi scenario)
        if (stream is null)
        {
            var propertyId = PropertyId.From("prop-demo");
            var owner      = ClientIdentity.FromHomeowner("ho-demo-owner");
            stream = DeviceReadingStream.Create(
                Shared.Domain.Model.ValueObjects.DeviceId.From(deviceId),
                propertyId, owner);
            await streamRepository.AddAsync(stream);
            await unitOfWork.CompleteAsync();
            logger.LogInformation("[Edge] Auto-created demo stream for device {DeviceId}.", deviceId);
        }

        var t = stream.CustomThresholds;
        var result = new DeviceThresholdResource(
            deviceId,
            new DeviceThresholdValues(
                t.NormalLimitAmps, t.AlertLimitAmps,
                t.NominalVoltage, t.MaxConsumptionWatts,
                t.MaxCurrentAmps, t.MinPowerFactor,
                t.NominalFrequency, t.DisconnectionThresholdMin)
        );
        return Ok(result);
    }
}
