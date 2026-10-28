using Hampcoders.Electrolink.API.Assets.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Application.Internal.CommandServices;
using HumanAuthorize = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AuthorizeAttribute;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Assets.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Assets.Interfaces.REST.Transform;
using Microsoft.AspNetCore.Mvc;

namespace Hampcoders.Electrolink.API.Assets.Interfaces.REST.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class IoTDevicesController : ControllerBase
{
    private readonly IIoTDeviceCommandService _commandService;
    private readonly IIoTDeviceQueryService _queryService;

    public IoTDevicesController(
        IIoTDeviceCommandService commandService,
        IIoTDeviceQueryService queryService)
    {
        _commandService = commandService;
        _queryService = queryService;
    }

    // Canonical stock registration route; the legacy POST remains an equally protected compatibility alias.
    [HttpPost("~/api/v1/iot-devices")]
    [HttpPost]
    [HumanAuthorize]
    public async Task<IActionResult> RegisterDevice([FromBody] RegisterIoTDeviceResource resource, [FromServices] TechnicianDeviceStockService stock)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        if (actor.Status != EUserStatus.Active)
            return StatusCode(StatusCodes.Status403Forbidden);
        try
        {
            var device = await stock.Register(actor.Id, resource.SerialNumber, resource.PlainTextApiKey, resource.FirmwareVersion);
            var deviceResource = IoTDeviceResourceFromEntityAssembler.ToResourceFromEntity(device);
            return CreatedAtAction(nameof(GetStockDevice), new { id = device.Id.Value }, deviceResource);
        }
        catch (TechnicianStockAccessDeniedException) { return StatusCode(StatusCodes.Status403Forbidden); }
        catch (DuplicateAssetException) { return Conflict(new { message = "Device serial number already exists." }); }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation,
                ConstraintName: "i_x_io_t_devices_serial_number" })
        { return Conflict(new { message = "Device serial number already exists." }); }
    }

    [HttpGet("{id}"), HumanAuthorize]
    public async Task<IActionResult> GetById(string id)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        var device = await _queryService.Handle(new GetOwnedIoTDeviceByIdQuery(actor.Id, id));
        if (device is null) return NotFound();
        var resource = IoTDeviceResourceFromEntityAssembler.ToResourceFromEntity(device);
        return Ok(resource);
    }

    [HttpGet("~/api/v1/iot-devices/{id}"), HumanAuthorize]
    public async Task<IActionResult> GetStockDevice(string id, [FromServices] TechnicianDeviceStockService stock)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try
        {
            var device = await stock.GetDevice(actor.Id, id);
            return device is null ? NotFound() : Ok(IoTDeviceResourceFromEntityAssembler.ToResourceFromEntity(device));
        }
        catch (TechnicianStockAccessDeniedException) { return StatusCode(StatusCodes.Status403Forbidden); }
    }

    [HttpGet("~/api/v1/iot-devices/my-stock"), HumanAuthorize]
    public async Task<IActionResult> GetMyStock([FromServices] TechnicianDeviceStockService stock, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try
        {
            var result = await stock.GetStock(actor.Id, page, pageSize);
            return Ok(new { items = result.Items.Select(d => new { id = d.Id.Value, serialNumber = d.SerialNumber.Value, d.FirmwareVersion, d.Status }),
                page, pageSize, result.TotalCount, result.AvailableCount, hasMore = (long)page * pageSize < result.TotalCount });
        }
        catch (TechnicianStockAccessDeniedException) { return StatusCode(StatusCodes.Status403Forbidden); }
        catch (ArgumentOutOfRangeException) { return BadRequest(new { message = "Invalid stock page." }); }
    }

    [HttpGet, HumanAuthorize]
    public async Task<IActionResult> GetAll([FromServices] TechnicianDeviceStockService stock)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try { return Ok((await stock.GetLegacyStock(actor.Id)).Select(IoTDeviceResourceFromEntityAssembler.ToResourceFromEntity)); }
        catch (TechnicianStockAccessDeniedException) { return StatusCode(StatusCodes.Status403Forbidden); }
    }

    [HttpGet("by-property/{propertyId}")]
    public async Task<IActionResult> GetByPropertyId(string propertyId)
    {
        var devices = await _queryService.Handle(new GetIoTDevicesByPropertyIdQuery(propertyId));
        var resources = devices.Select(IoTDeviceResourceFromEntityAssembler.ToResourceFromEntity);
        return Ok(resources);
    }

    [HttpGet("by-status/{status}"), HumanAuthorize]
    public async Task<IActionResult> GetByStatus(EDeviceStatus status, [FromServices] TechnicianDeviceStockService stock)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try { return Ok((await stock.GetLegacyStock(actor.Id, status)).Select(IoTDeviceResourceFromEntityAssembler.ToResourceFromEntity)); }
        catch (TechnicianStockAccessDeniedException) { return StatusCode(StatusCodes.Status403Forbidden); }
    }

    [HttpGet("active-count/{ownerId}"), HumanAuthorize]
    public async Task<IActionResult> GetActiveDeviceCount(string ownerId, [FromServices] TechnicianDeviceStockService stock)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try
        {
            var count = await stock.GetLegacyActiveCount(actor.Id, ownerId);
            return count.HasValue ? Ok(new ActiveDeviceCountResource(count.Value)) : NotFound();
        }
        catch (TechnicianStockAccessDeniedException) { return StatusCode(StatusCodes.Status403Forbidden); }
    }

    [HttpPost("{id}/assign/{propertyId}"), HumanAuthorize]
    public async Task<IActionResult> AssignToProperty(string id, string propertyId, [FromBody] AssignDeviceToPropertyResource resource,
        [FromServices] Hampcoders.Electrolink.API.Assets.Application.Internal.CommandServices.DeviceAssignmentService assignments)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        if (actor.Status != EUserStatus.Active) return NotFound(new { message = "Resource not found." });
        try
        {
            var device = await assignments.Handle(new AssignRequestedDeviceCommand(actor.Id, id, propertyId, resource.InstallationRequestId));
            return Ok(IoTDeviceResourceFromEntityAssembler.ToResourceFromEntity(device));
        }
        catch (DeviceAssignmentUnavailableException) { return NotFound(new { message = "Resource not found." }); }
        catch (DeviceAssignmentConflictException) { return Conflict(new { message = "Device is not free stock." }); }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException ex)
            when (ex.Entries.Any(e => e.Entity is Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates.IoTDevice device && device.Id.Value == id))
        { return Conflict(new { message = "Device is not free stock." }); }
    }

    [HttpPut("{id}/install/{propertyId}"), HumanAuthorize]
    public async Task<IActionResult> RecordInstallation(string id, string propertyId, [FromBody] RecordDeviceInstallationResource resource,
        [FromServices] Hampcoders.Electrolink.API.Assets.Application.Internal.CommandServices.DeviceInstallationService installations)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try
        {
            var device = await installations.Handle(new RecordAssignedDeviceInstallationCommand(actor.Id, id, propertyId,
                resource.TechnicianId, resource.FirmwareVersion, resource.InstalledAt));
            return Ok(IoTDeviceResourceFromEntityAssembler.ToResourceFromEntity(device));
        }
        catch (DeviceInstallationUnavailableException) { return NotFound(new { message = "Resource not found." }); }
        catch (DeviceInstallationStateException) { return Conflict(new { message = "Device is not awaiting installation." }); }
    }

    [HttpPut("{id}/connection-status")]
    public async Task<IActionResult> UpdateConnectionStatus(string id, [FromBody] UpdateDeviceConnectionStatusResource resource)
    {
        var command = new UpdateDeviceConnectionStatusCommand(id, resource.NewConnectionStatus, resource.LastReadingAt);
        var device = await _commandService.Handle(command);
        var deviceResource = IoTDeviceResourceFromEntityAssembler.ToResourceFromEntity(device);
        return Ok(deviceResource);
    }

    [HttpPut("{id}/maintenance")]
    public async Task<IActionResult> SendToMaintenance(string id, [FromBody] SendDeviceToMaintenanceResource resource)
    {
        var command = new SendDeviceToMaintenanceCommand(id, resource.Reason, resource.ExpectedReturnDate);
        var device = await _commandService.Handle(command);
        var deviceResource = IoTDeviceResourceFromEntityAssembler.ToResourceFromEntity(device);
        return Ok(deviceResource);
    }

    [HttpPut("{id}/reinstall")]
    public async Task<IActionResult> Reinstall(string id, [FromBody] ReinstallDeviceResource resource)
    {
        var command = new ReinstallDeviceCommand(id, resource.TechnicianId, resource.FirmwareVersion, resource.ReinstalledAt);
        var device = await _commandService.Handle(command);
        var deviceResource = IoTDeviceResourceFromEntityAssembler.ToResourceFromEntity(device);
        return Ok(deviceResource);
    }

    [HttpPost("{id}/decommission"), HumanAuthorize]
    public async Task<IActionResult> Decommission(string id, [FromBody] DecommissionDeviceResource resource)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try
        {
            await _commandService.Handle(new DecommissionOwnedDeviceCommand(actor.Id, id, resource.Reason));
            return NoContent();
        }
        catch (DeviceRetirementUnavailableException)
        { return NotFound(new { message = "Resource not found." }); }
    }
}
