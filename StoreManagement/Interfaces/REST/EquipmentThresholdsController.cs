using Hampcoders.Electrolink.API.Shared.Interfaces.REST;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Microsoft.AspNetCore.Mvc;
using HumanAuthorize = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AuthorizeAttribute;
namespace Hampcoders.Electrolink.API.StoreManagement.Interfaces.REST;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateEquipmentThresholdsResource(
    [Range(1, 1000)] float NominalVoltage,
    [Range(1, 10_000_000)] float MaxConsumptionWatts,
    [Range(0.01, 10_000)] float MaxCurrentAmps,
    [Range(0, 1)] float MinPowerFactor,
    [Range(1, 1000)] float NominalFrequency,
    [Range(1, 1440)] int DisconnectionThresholdMin,
    [Range(0.0001, 10_000)] float NormalLimitAmps,
    [Range(0.0001, 10_000)] float AlertLimitAmps);

[ApiController, HumanAuthorize]
[Route("api/v1/stores/{storeId}/areas/{areaId}/equipment/{equipmentId}/thresholds")]
[Produces("application/json")]
public sealed class EquipmentThresholdsController(EquipmentThresholdService service) : ControllerBase
{
    [HttpGet]
    public Task<ActionResult<EquipmentThresholdsResource>> Get(string storeId, string areaId, string equipmentId) =>
        Execute(actor => service.Handle(new GetEquipmentThresholdsQuery(actor.Id, storeId, areaId, equipmentId), HttpContext.RequestAborted));

    [HttpPut]
    public Task<ActionResult<EquipmentThresholdsResource>> Put(string storeId, string areaId, string equipmentId, UpdateEquipmentThresholdsResource body) =>
        Execute(actor => service.Handle(new UpdateEquipmentThresholdsCommand(actor.Id, storeId, areaId, equipmentId,
            new EquipmentThresholdsResource(body.NominalVoltage, body.MaxConsumptionWatts, body.MaxCurrentAmps, body.MinPowerFactor,
                body.NominalFrequency, body.DisconnectionThresholdMin, body.NormalLimitAmps, body.AlertLimitAmps)), HttpContext.RequestAborted));

    private async Task<ActionResult<T>> Execute<T>(Func<User, Task<T>> action)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try { return Ok(await action(actor)); }
        catch (HierarchyNotFoundException notFound) { return NotFound(ErrorBodies.Coded("Resource not found.", notFound.ErrorCode)); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
}
