using Hampcoders.Electrolink.API.Assets.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
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

    [HttpPost]
    public async Task<IActionResult> RegisterDevice([FromBody] RegisterIoTDeviceResource resource)
    {
        var command = RegisterIoTDeviceCommandFromResourceAssembler.ToCommandFromResource(resource);
        var device = await _commandService.Handle(command);
        var deviceResource = IoTDeviceResourceFromEntityAssembler.ToResourceFromEntity(device);
        return CreatedAtAction(nameof(GetById), new { id = device.Id.Value }, deviceResource);
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

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var devices = await _queryService.Handle(new GetAllIoTDevicesQuery());
        var resources = devices.Select(IoTDeviceResourceFromEntityAssembler.ToResourceFromEntity);
        return Ok(resources);
    }

    [HttpGet("by-property/{propertyId}")]
    public async Task<IActionResult> GetByPropertyId(string propertyId)
    {
        var devices = await _queryService.Handle(new GetIoTDevicesByPropertyIdQuery(propertyId));
        var resources = devices.Select(IoTDeviceResourceFromEntityAssembler.ToResourceFromEntity);
        return Ok(resources);
    }

    [HttpGet("by-status/{status}")]
    public async Task<IActionResult> GetByStatus(EDeviceStatus status)
    {
        var devices = await _queryService.Handle(new GetIoTDevicesByStatusQuery(status));
        var resources = devices.Select(IoTDeviceResourceFromEntityAssembler.ToResourceFromEntity);
        return Ok(resources);
    }

    [HttpGet("active-count/{ownerId}")]
    public async Task<IActionResult> GetActiveDeviceCount(string ownerId)
    {
        var count = await _queryService.Handle(new GetActiveDeviceCountByOwnerIdQuery(ownerId));
        return Ok(new ActiveDeviceCountResource(count));
    }

    [HttpPost("{id}/assign/{propertyId}")]
    public async Task<IActionResult> AssignToProperty(string id, string propertyId, [FromBody] AssignDeviceToPropertyResource resource)
    {
        var command = new AssignDeviceToPropertyCommand(id, propertyId, resource.InstallationRequestId);
        var device = await _commandService.Handle(command);
        var deviceResource = IoTDeviceResourceFromEntityAssembler.ToResourceFromEntity(device);
        return Ok(deviceResource);
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
