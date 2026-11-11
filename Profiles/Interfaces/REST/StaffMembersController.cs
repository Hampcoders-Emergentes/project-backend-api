using System.Net.Mime;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Profiles.Domain.Services;
using Hampcoders.Electrolink.API.Profiles.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Profiles.Interfaces.REST.Transform;
using Microsoft.AspNetCore.Mvc;
using Hampcoders.Electrolink.API.Shared.Infrastructure.OpenApi;

namespace Hampcoders.Electrolink.API.Profiles.Interfaces.REST;

[Authorize]
[ApiController]
[Route("api/v1/staff-members")]
[Produces(MediaTypeNames.Application.Json)]
[ApiDocTag("Staff Member Management (Admin only)")]
public class StaffMembersController(
    IStaffMemberCommandService staffMemberCommandService,
    IStaffMemberQueryService staffMemberQueryService) : ControllerBase
{
    [HttpPost]
    [RequireAccessRole(
        Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects.AccessRole.Admin,
        Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects.AccessRole.SuperAdmin)]
    [ApiDocOperation(Summary = "Create a staff member profile")]
    public async Task<IActionResult> CreateStaffMember([FromBody] CreateStaffMemberResource resource)
    {
        try
        {
            var command = CreateStaffMemberCommandFromResourceAssembler.ToCommandFromResource(resource);
            await staffMemberCommandService.Handle(command);
            return Ok(new { message = "Staff member created successfully." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id}")]
    [RequireAccessRole(
        Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects.AccessRole.Admin,
        Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects.AccessRole.SuperAdmin,
        Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects.AccessRole.FieldManager)]
    public async Task<IActionResult> GetStaffMemberById(string id)
    {
        var staffMember = await staffMemberQueryService.Handle(new GetStaffMemberByIdQuery(id));
        if (staffMember is null) return NotFound();
        return Ok(StaffMemberResourceFromEntityAssembler.ToResourceFromEntity(staffMember));
    }

    [HttpGet]
    [RequireAccessRole(
        Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects.AccessRole.Admin,
        Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects.AccessRole.SuperAdmin)]
    public async Task<IActionResult> GetAllStaffMembers()
    {
        var members = await staffMemberQueryService.Handle(new GetAllStaffMembersQuery());
        var resources = members.Select(StaffMemberResourceFromEntityAssembler.ToResourceFromEntity);
        return Ok(resources);
    }

    [HttpPut("{id}/zone")]
    [RequireAccessRole(
        Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects.AccessRole.Admin,
        Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects.AccessRole.SuperAdmin)]
    public async Task<IActionResult> AssignZone(string id, [FromBody] AssignStaffZoneResource resource)
    {
        try
        {
            var command = new AssignStaffZoneCommand(
                id, resource.Region, resource.CenterLatitude, resource.CenterLongitude, resource.RadiusKm);
            await staffMemberCommandService.Handle(command);
            return Ok(new { message = "Zone assigned successfully." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}/iot-certification")]
    [RequireAccessRole(
        Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects.AccessRole.Admin,
        Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects.AccessRole.SuperAdmin)]
    public async Task<IActionResult> GrantIoTCertification(string id)
    {
        try
        {
            await staffMemberCommandService.Handle(new GrantStaffIoTCertificationCommand(id));
            return Ok(new { message = "IoT certification granted." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id}/iot-certification")]
    [RequireAccessRole(
        Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects.AccessRole.Admin,
        Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects.AccessRole.SuperAdmin)]
    public async Task<IActionResult> RevokeIoTCertification(string id)
    {
        try
        {
            await staffMemberCommandService.Handle(new RevokeStaffIoTCertificationCommand(id));
            return Ok(new { message = "IoT certification revoked." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}/deactivate")]
    [RequireAccessRole(
        Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects.AccessRole.Admin,
        Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects.AccessRole.SuperAdmin)]
    public async Task<IActionResult> DeactivateStaffMember(string id)
    {
        try
        {
            await staffMemberCommandService.Handle(new DeactivateStaffMemberCommand(id));
            return Ok(new { message = "Staff member deactivated." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
