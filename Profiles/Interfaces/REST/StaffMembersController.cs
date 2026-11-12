using System.Net.Mime;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Profiles.Domain.Services;
using Hampcoders.Electrolink.API.Profiles.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Profiles.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Idempotency;
using Hampcoders.Electrolink.API.Shared.Interfaces.REST.Resources;
using Microsoft.AspNetCore.Mvc;
using Hampcoders.Electrolink.API.Shared.Infrastructure.OpenApi;
using AccessRole = Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects.AccessRole;

namespace Hampcoders.Electrolink.API.Profiles.Interfaces.REST;

/// <summary>
///     Staff member profiles. Mutations return the resulting <see cref="StaffMemberResource"/> (201 on creation, 200 on
///     updates); revoking the IoT certification is a DELETE and returns 204.
/// </summary>
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
    [RequireAccessRole(AccessRole.Admin, AccessRole.SuperAdmin)]
    [IdempotentEndpoint]
    [ApiDocOperation(Summary = "Create a staff member profile")]
    [ProducesResponseType(typeof(StaffMemberResource), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateStaffMember([FromBody] CreateStaffMemberResource resource)
    {
        try
        {
            var command = CreateStaffMemberCommandFromResourceAssembler.ToCommandFromResource(resource);
            var staffMember = await staffMemberCommandService.Handle(command);
            var response = StaffMemberResourceFromEntityAssembler.ToResourceFromEntity(staffMember);
            return CreatedAtAction(nameof(GetStaffMemberById), new { id = response.Id }, response);
        }
        // A profile already exists for this user.
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("{id}")]
    [RequireAccessRole(AccessRole.Admin, AccessRole.SuperAdmin, AccessRole.FieldManager)]
    [ProducesResponseType(typeof(StaffMemberResource), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStaffMemberById(string id)
    {
        var staffMember = await staffMemberQueryService.Handle(new GetStaffMemberByIdQuery(id));
        if (staffMember is null) return NotFound(new { message = "Staff member not found." });
        return Ok(StaffMemberResourceFromEntityAssembler.ToResourceFromEntity(staffMember));
    }

    [HttpGet]
    [RequireAccessRole(AccessRole.Admin, AccessRole.SuperAdmin)]
    [ProducesResponseType(typeof(PagedResponse<StaffMemberResource>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllStaffMembers([FromQuery] PageRequest paging)
    {
        var members = await staffMemberQueryService.Handle(new GetAllStaffMembersQuery());
        return Ok(PagedResponse.From(members.Select(StaffMemberResourceFromEntityAssembler.ToResourceFromEntity), paging));
    }

    [HttpPut("{id}/zone")]
    [RequireAccessRole(AccessRole.Admin, AccessRole.SuperAdmin)]
    [ProducesResponseType(typeof(StaffMemberResource), StatusCodes.Status200OK)]
    public Task<IActionResult> AssignZone(string id, [FromBody] AssignStaffZoneResource resource) =>
        MutateAsync(id, () => staffMemberCommandService.Handle(new AssignStaffZoneCommand(
            id, resource.Region, resource.CenterLatitude, resource.CenterLongitude, resource.RadiusKm)));

    [HttpPut("{id}/iot-certification")]
    [RequireAccessRole(AccessRole.Admin, AccessRole.SuperAdmin)]
    [ProducesResponseType(typeof(StaffMemberResource), StatusCodes.Status200OK)]
    public Task<IActionResult> GrantIoTCertification(string id) =>
        MutateAsync(id, () => staffMemberCommandService.Handle(new GrantStaffIoTCertificationCommand(id)));

    [HttpDelete("{id}/iot-certification")]
    [RequireAccessRole(AccessRole.Admin, AccessRole.SuperAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeIoTCertification(string id)
    {
        try
        {
            await staffMemberCommandService.Handle(new RevokeStaffIoTCertificationCommand(id));
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}/deactivate")]
    [RequireAccessRole(AccessRole.Admin, AccessRole.SuperAdmin)]
    [ProducesResponseType(typeof(StaffMemberResource), StatusCodes.Status200OK)]
    public Task<IActionResult> DeactivateStaffMember(string id) =>
        MutateAsync(id, () => staffMemberCommandService.Handle(new DeactivateStaffMemberCommand(id)));

    /// <summary>Runs the command and answers with the updated staff member.</summary>
    private async Task<IActionResult> MutateAsync(string id, Func<Task> command)
    {
        try
        {
            await command();
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        var staffMember = await staffMemberQueryService.Handle(new GetStaffMemberByIdQuery(id));
        return staffMember is null
            ? NotFound(new { message = "Staff member not found." })
            : Ok(StaffMemberResourceFromEntityAssembler.ToResourceFromEntity(staffMember));
    }
}
