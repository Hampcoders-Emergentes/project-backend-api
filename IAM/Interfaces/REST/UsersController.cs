using System.Net.Mime;
using System.Security.Claims;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Queries;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes;
using Hampcoders.Electrolink.API.IAM.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.IAM.Interfaces.REST.Transform;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Hampcoders.Electrolink.API.IAM.Interfaces.REST;

[Authorize]
[ApiController]
[Route("api/v1/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Available User endpoints")]
public class UsersController(IUserQueryService userQueryService, IUserCommandService userCommandService, ILogger<UsersController> logger) : ControllerBase
{
    [HttpGet("{id}")]
    [SwaggerOperation(
        Summary = "Get a user by its id",
        Description = "Get a user by its id",
        OperationId = "GetUserById")]
    [SwaggerResponse(StatusCodes.Status200OK, "The user was found", typeof(UserResource))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "You are not authorized to view this user")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "User not found")]
    public async Task<IActionResult> GetUserById(string id)
    {
        var authenticatedUser = HttpContext.Items["User"] as User;
        if (authenticatedUser == null || authenticatedUser.Id.Value != id)
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "You are not authorized to view this user."
            });

        var getUserByIdQuery = new GetUserByIdQuery(id);
        var user = await userQueryService.Handle(getUserByIdQuery);
        if (user == null) return NotFound(new { message = "User not found" });
        var userResource = UserResourceFromEntityAssembler.ToResourceFromEntity(user);
        return Ok(userResource);
    }

    [HttpGet]
    [RequireAccessRole(AccessRole.Admin, AccessRole.SuperAdmin)]
    [SwaggerOperation(
        Summary = "Get all users",
        Description = "Get all users with pagination",
        OperationId = "GetAllUsers")]
    [SwaggerResponse(StatusCodes.Status200OK, "The users were found", typeof(IEnumerable<UserResource>))]
    public async Task<IActionResult> GetAllUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var getAllUsersQuery = new GetAllUsersQuery(page, pageSize);
        var users = await userQueryService.Handle(getAllUsersQuery);
        var userResources = users.Select(UserResourceFromEntityAssembler.ToResourceFromEntity);
        return Ok(userResources);
    }

    [HttpPut("{id}/password")]
    [SwaggerOperation(
        Summary = "Update a user's password",
        Description = "Allows an authenticated user to update their password. Requires current password verification.",
        OperationId = "UpdatePassword")]
    [SwaggerResponse(StatusCodes.Status200OK, "Password updated successfully")]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Invalid current password, new password, or user not found")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Unauthorized")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Forbidden - User ID in token does not match route ID")]
    public async Task<IActionResult> UpdatePassword(string id, [FromBody] UpdatePasswordResource resource)
    {
        try
        {
            // **CORRECCIÓN**: Obtener el usuario desde HttpContext.Items
            var authenticatedUser = HttpContext.Items["User"] as User;
            if (authenticatedUser == null || authenticatedUser.Id.Value != id)
            {
                logger.LogWarning($"[IAM Controller] Intento de actualizar contraseña por usuario no autorizado. TokenUserId: {authenticatedUser?.Id}, RouteId: {id}.");
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = "You are not authorized to update this user's password."
                });
            }
            
            var command = UpdatePasswordCommandFromResourceAssembler.ToCommandFromResource(id, resource);
            await userCommandService.Handle(command);
            return Ok(new { message = "Password updated successfully" });
        }
        catch (ArgumentException ex)
        {
            logger.LogError(ex, $"[IAM Controller] Error al actualizar contraseña para ID: {id}. Mensaje: {ex.Message}.");
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"[IAM Controller] Error inesperado al actualizar contraseña para ID: {id}.");
            return BadRequest(new { message = ex.Message });
        }
    }
}