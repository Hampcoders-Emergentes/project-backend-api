using System.Net.Mime;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Interfaces.REST.Resources;
using Microsoft.AspNetCore.Mvc;
using Hampcoders.Electrolink.API.Shared.Infrastructure.OpenApi;

namespace Hampcoders.Electrolink.API.Planning.Interfaces.REST;

[Authorize]
[ApiController]
[Route("api/v1/dispatch")]
[Produces(MediaTypeNames.Application.Json)]
[ApiDocTag("Dispatch Management (Admin / FieldManager)")]
public class DispatchController(
    IServiceAssignmentCommandService assignmentCommandService,
    IServiceRequestCommandService requestCommandService) : ControllerBase
{
    [HttpPost("assign-staff")]
    [RequireAccessRole(
        Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects.AccessRole.Admin,
        Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects.AccessRole.SuperAdmin,
        Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects.AccessRole.FieldManager)]
    [ApiDocOperation(Summary = "Assign a staff member (Maker) to a service request")]
    public async Task<IActionResult> AssignStaff([FromBody] AssignStaffResource resource)
    {
        try
        {
            var command = new AssignStaffToServiceCommand(
                RequestId.From(resource.RequestId),
                resource.StaffMemberId);
            await assignmentCommandService.Handle(command);
            return Ok(new { message = "Staff member assigned successfully." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
