using System.Net.Mime;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes;
using Hampcoders.Electrolink.API.IAM.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.IAM.Interfaces.REST.Transform;
using Microsoft.AspNetCore.Mvc;
using Hampcoders.Electrolink.API.Shared.Infrastructure.OpenApi;

namespace Hampcoders.Electrolink.API.IAM.Interfaces.REST;

[Authorize]
[ApiController]
[Route("api/v1/staff-users")]
[Produces(MediaTypeNames.Application.Json)]
[ApiDocTag("Staff User Management (Admin only)")]
public class StaffUsersController(IStaffUserCommandService staffUserCommandService) : ControllerBase
{
    [HttpPost]
    [RequireAccessRole(Domain.Model.ValueObjects.AccessRole.Admin, Domain.Model.ValueObjects.AccessRole.SuperAdmin)]
    [ApiDocOperation(
        Summary = "Create a staff user",
        Description = "Creates a staff user (Maker, Admin, FieldManager). Only accessible by Admin/SuperAdmin.",
        OperationId = "CreateStaffUser")]
    [ApiDocResponse(StatusCodes.Status201Created, "Staff user created", typeof(StaffUserResource))]
    [ApiDocResponse(StatusCodes.Status400BadRequest, "Invalid data")]
    [ApiDocResponse(StatusCodes.Status403Forbidden, "Forbidden")]
    public async Task<IActionResult> CreateStaffUser([FromBody] CreateStaffUserResource resource)
    {
        try
        {
            var command = CreateStaffUserCommandFromResourceAssembler.ToCommandFromResource(resource);
            var userId = await staffUserCommandService.Handle(command);
            var responseResource = new StaffUserResource(userId, resource.Email, resource.AccessRole);
            return CreatedAtAction(nameof(CreateStaffUser), responseResource);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
