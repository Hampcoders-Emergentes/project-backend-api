using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Microsoft.AspNetCore.Mvc;
using HumanAuthorize = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AuthorizeAttribute;
namespace Hampcoders.Electrolink.API.StoreManagement.Interfaces.REST;

/// <summary>Live operational views of a store: equipment risk, electrical grid status and sensor supervision.</summary>
[ApiController, HumanAuthorize]
[Route("api/v1/stores/{storeId}")]
[Produces("application/json")]
public sealed class StoreOperationsController(StoreOperationsService service) : ControllerBase
{
    /// <summary>Risk level (SAFE, WARNING, CRITICAL_RISK, DISCONNECTED, UNKNOWN) of one equipment, with the reasons.</summary>
    [HttpGet("areas/{areaId}/equipment/{equipmentId}/risk-level")]
    public Task<ActionResult<EquipmentRiskResource>> GetRiskLevel(string storeId, string areaId, string equipmentId) =>
        Execute(actor => service.Handle(new GetEquipmentRiskLevelQuery(actor.Id, storeId, areaId, equipmentId), HttpContext.RequestAborted));

    /// <summary>Store-wide electrical status aggregated from the latest fresh reading of every equipment.</summary>
    [HttpGet("grid-status")]
    public Task<ActionResult<StoreElectricalGridStatusResource>> GetGridStatus(string storeId) =>
        Execute(actor => service.Handle(new GetStoreElectricalGridStatusQuery(actor.Id, storeId), HttpContext.RequestAborted));

    /// <summary>Connection status of every linked sensor, from its telemetry heartbeat.</summary>
    [HttpGet("sensors/connectivity")]
    public Task<ActionResult<StoreSensorsConnectivityResource>> GetSensorsConnectivity(string storeId) =>
        Execute(actor => service.Handle(new GetSensorsConnectivityQuery(actor.Id, storeId), HttpContext.RequestAborted));

    private async Task<ActionResult<T>> Execute<T>(Func<User, Task<T>> action)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try { return Ok(await action(actor)); }
        catch (HierarchyNotFoundException) { return NotFound(new { message = "Resource not found." }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
}
