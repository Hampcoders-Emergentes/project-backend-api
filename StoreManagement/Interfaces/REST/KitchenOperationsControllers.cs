using Hampcoders.Electrolink.API.Shared.Interfaces.REST;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Hampcoders.Electrolink.API.Shared.Interfaces.REST.Resources;
using Microsoft.AspNetCore.Mvc;
using HumanAuthorize = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AuthorizeAttribute;
namespace Hampcoders.Electrolink.API.StoreManagement.Interfaces.REST;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SafeToCleanRuleInputResource(
    [Range(0, 1)] float MaxDeEnergizedCurrentAmps,
    [Range(1, 30)] int MaxReadingAgeMinutes,
    bool RequireNoActiveCriticalAlerts = true);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateShutdownProtocolResource(
    [Required, MinLength(1), MaxLength(30)] IReadOnlyList<string> ShutdownSteps,
    [Required, MinLength(1), MaxLength(30)] IReadOnlyList<string> EmergencyIsolationSteps,
    [Required] SafeToCleanRuleInputResource IsSafeToCleanRule);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScheduleStaffShiftResource(
    [Required, StringLength(60)] string WorkerCode,
    [Required, StringLength(120)] string WorkerName,
    [Required, StringLength(30)] string Role,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordShiftHandoverResource(
    [Required, StringLength(60)] string WorkerId,
    [StringLength(120)] string? WorkerName,
    DateOnly? ShiftDate,
    [StringLength(2000)] string? Notes);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StartPanelPauseResource([Range(15, 300)] int DurationSeconds = 60, [StringLength(100)] string? PanelId = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EquipmentPlacementResource(
    [Required, StringLength(42)] string EquipmentId,
    double CoordinateX, double CoordinateY,
    [Range(0.001, 100_000)] double Width, [Range(0.001, 100_000)] double Height,
    [Range(0, 100)] int ZIndex = 0);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateStoreLayoutResource(
    [Range(1, 100_000)] double? CanvasWidth,
    [Range(1, 100_000)] double? CanvasHeight,
    [MaxLength(500)] IReadOnlyList<EquipmentPlacementResource>? Items,
    [MaxLength(500)] IReadOnlyList<string>? RemoveEquipmentIds);

/// <summary>Shared error mapping of the kitchen operation endpoints.</summary>
public abstract class KitchenControllerBase : ControllerBase
{
    protected async Task<ActionResult<T>> Execute<T>(Func<User, Task<T>> action, Func<T, ActionResult<T>>? created = null)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try { var value = await action(actor); return created != null ? created(value) : Ok(value); }
        catch (HierarchyNotFoundException notFound) { return NotFound(ErrorBodies.Coded("Resource not found.", notFound.ErrorCode)); }
        catch (PanelPauseRejectedException e) { return Conflict(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
}

/// <summary>Sanitation safety of kitchen equipment: de-energization check, shutdown protocol and incident guide.</summary>
[ApiController, HumanAuthorize]
[Route("api/v1/stores/{storeId}")]
[Produces("application/json")]
public sealed class KitchenSafetyController(KitchenSafetyService service) : KitchenControllerBase
{
    /// <summary>Whether the equipment circuit is de-energized and safe to clean, from its latest telemetry.</summary>
    [HttpGet("areas/{areaId}/equipment/{equipmentId}/safety-status")]
    public Task<ActionResult<EquipmentSafetyStatusResource>> GetSafetyStatus(string storeId, string areaId, string equipmentId) =>
        Execute(actor => service.Handle(new GetEquipmentSafetyStatusQuery(actor.Id, storeId, areaId, equipmentId), HttpContext.RequestAborted));

    /// <summary>Step-by-step safe shutdown and emergency power isolation instructions (generic ones until customised).</summary>
    [HttpGet("areas/{areaId}/equipment/{equipmentId}/shutdown-protocol")]
    public Task<ActionResult<ShutdownProtocolResource>> GetShutdownProtocol(string storeId, string areaId, string equipmentId) =>
        Execute(actor => service.Handle(new GetShutdownProtocolQuery(actor.Id, storeId, areaId, equipmentId), HttpContext.RequestAborted));

    /// <summary>Replaces the equipment's protocol and its safe-to-clean rule.</summary>
    [HttpPut("areas/{areaId}/equipment/{equipmentId}/shutdown-protocol")]
    public Task<ActionResult<ShutdownProtocolResource>> PutShutdownProtocol(string storeId, string areaId, string equipmentId,
        UpdateShutdownProtocolResource body) =>
        Execute(actor => service.Handle(new UpdateShutdownProtocolCommand(actor.Id, storeId, areaId, equipmentId, body.ShutdownSteps,
            body.EmergencyIsolationSteps, new SafeToCleanRuleResource(body.IsSafeToCleanRule.MaxDeEnergizedCurrentAmps,
                body.IsSafeToCleanRule.MaxReadingAgeMinutes, body.IsSafeToCleanRule.RequireNoActiveCriticalAlerts)), HttpContext.RequestAborted));

    /// <summary>Emergency first-aid and isolation guide for electrical incidents. Cacheable by the panel for offline use.</summary>
    [HttpGet("safety-guides/electrical-incident")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Client)]
    public Task<ActionResult<ElectricalIncidentGuideResource>> GetElectricalIncidentGuide(string storeId) =>
        Execute(actor => service.Handle(new GetElectricalIncidentGuideQuery(actor.Id, storeId), HttpContext.RequestAborted));
}

/// <summary>Shift roster (on-duty workers) and shift handover log of a store.</summary>
[ApiController, HumanAuthorize]
[Route("api/v1/stores/{storeId}/shifts")]
[Produces("application/json")]
public sealed class ShiftOperationsController(ShiftOperationsService service) : KitchenControllerBase
{
    /// <summary>Schedules a worker on the store floor. Roles: OPERATIONAL_STAFF, TECHNICIAN, STORE_MANAGER.</summary>
    [HttpPost("assignments")]
    public Task<ActionResult<StaffShiftResource>> Schedule(string storeId, ScheduleStaffShiftResource body) =>
        Execute(actor => service.Handle(new ScheduleStaffShiftCommand(actor.Id, storeId, body.WorkerCode, body.WorkerName, body.Role,
            body.StartsAt, body.EndsAt), HttpContext.RequestAborted), value => Created($"/api/v1/stores/{storeId}/shifts/assignments", value));

    /// <summary>Shifts overlapping [from, to) (ISO timestamps with offset); defaults to the last 12 and next 24 hours.</summary>
    [HttpGet("assignments")]
    public Task<ActionResult<PagedResponse<StaffShiftResource>>> List(string storeId, [FromQuery] PageRequest paging,
        [FromQuery] string? from = null, [FromQuery] string? to = null) =>
        Execute(async actor => PagedResponse.From(await service.Handle(new ListStaffShiftsQuery(actor.Id, storeId, from, to), HttpContext.RequestAborted),
            paging));

    [HttpDelete("assignments/{shiftId}")]
    public async Task<IActionResult> Remove(string storeId, string shiftId)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try
        {
            await service.Handle(new RemoveStaffShiftCommand(actor.Id, storeId, shiftId), HttpContext.RequestAborted);
            return NoContent();
        }
        catch (HierarchyNotFoundException notFound) { return NotFound(ErrorBodies.Coded("Resource not found.", notFound.ErrorCode)); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }

    /// <summary>Workers on duty right now and the one accountable for incidents.</summary>
    [HttpGet("on-duty")]
    public Task<ActionResult<OnDutyStaffResource>> OnDuty(string storeId) =>
        Execute(actor => service.Handle(new GetOnDutyStaffQuery(actor.Id, storeId), HttpContext.RequestAborted));

    /// <summary>Closes the shift: snapshots every equipment's state and notifies the store manager.</summary>
    [HttpPost("handover")]
    public Task<ActionResult<ShiftHandoverResource>> Handover(string storeId, RecordShiftHandoverResource body) =>
        Execute(actor => service.Handle(new RecordShiftHandoverCommand(actor.Id, storeId, body.WorkerId, body.WorkerName, body.ShiftDate,
            body.Notes), HttpContext.RequestAborted), value => Created($"/api/v1/stores/{storeId}/shifts/handover", value));

    /// <summary>Handover log, newest first.</summary>
    [HttpGet("handover")]
    public Task<ActionResult<HierarchyPage<ShiftHandoverResource>>> ListHandovers(string storeId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        Execute(actor => service.Handle(new ListShiftHandoversQuery(actor.Id, storeId, page, pageSize), HttpContext.RequestAborted));
}

/// <summary>Touchscreen sanitation pause of the store's control panel. Critical alerts override it automatically.</summary>
[ApiController, HumanAuthorize]
[Route("api/v1/stores/{storeId}/panel/pause")]
[Produces("application/json")]
public sealed class PanelPauseController(PanelPauseService service) : KitchenControllerBase
{
    /// <summary>Locks touch input for 15 to 300 seconds. Rejected (409) while a critical alert is active.</summary>
    [HttpPost]
    public Task<ActionResult<PanelPauseResource>> Start(string storeId, StartPanelPauseResource? body) =>
        Execute(actor => service.Handle(new StartPanelPauseCommand(actor.Id, storeId, body?.DurationSeconds ?? 60, body?.PanelId),
            HttpContext.RequestAborted));

    [HttpGet]
    public Task<ActionResult<PanelPauseResource>> Get(string storeId) =>
        Execute(actor => service.Handle(new GetPanelPauseQuery(actor.Id, storeId), HttpContext.RequestAborted));

    /// <summary>
    ///     Unlocks the touchscreen before the pause expires. Deliberate exception to "DELETE → 204": answers 200 with the
    ///     resulting pause state (isPaused=false) so the panel can render it without a second request.
    /// </summary>
    [HttpDelete]
    [ProducesResponseType(typeof(PanelPauseResource), StatusCodes.Status200OK)]
    public Task<ActionResult<PanelPauseResource>> End(string storeId) =>
        Execute(actor => service.Handle(new EndPanelPauseCommand(actor.Id, storeId), HttpContext.RequestAborted));
}

/// <summary>Kitchen floor plan: equipment footprints for the interactive canvas.</summary>
[ApiController, HumanAuthorize]
[Route("api/v1/stores/{storeId}/layout")]
[Produces("application/json")]
public sealed class KitchenLayoutController(KitchenLayoutService service) : KitchenControllerBase
{
    /// <summary>Canvas size and every equipment with its footprint (if placed) and live risk level.</summary>
    [HttpGet]
    public Task<ActionResult<StoreLayoutResource>> Get(string storeId) =>
        Execute(actor => service.Handle(new GetStoreLayoutQuery(actor.Id, storeId), HttpContext.RequestAborted));

    /// <summary>
    ///     Places, moves or removes equipment (and optionally resizes the canvas). The resulting plan must keep every
    ///     footprint inside the canvas and must not overlap footprints on the same zIndex layer.
    /// </summary>
    [HttpPut]
    public Task<ActionResult<StoreLayoutResource>> Put(string storeId, UpdateStoreLayoutResource body) =>
        Execute(actor => service.Handle(new UpdateStoreLayoutCommand(actor.Id, storeId, body.CanvasWidth, body.CanvasHeight,
            (body.Items ?? []).Select(i => new EquipmentPlacementInput(i.EquipmentId, i.CoordinateX, i.CoordinateY, i.Width, i.Height, i.ZIndex)).ToList(),
            body.RemoveEquipmentIds ?? []), HttpContext.RequestAborted));
}
