using System.Net.Mime;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Commands;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes;
using Hampcoders.Electrolink.API.IAM.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.IAM.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Interfaces.REST;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Hampcoders.Electrolink.API.IAM.Interfaces.REST;

[Authorize]
[ApiController]
[Route("api/v1/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Available Authentication endpoints")]
public class AuthenticationController(IUserCommandService userCommandService) : ControllerBase
{
    /**
     * <summary>
     *     Sign in endpoint. It allows authenticating a user
     * </summary>
     * <param name="signInResource">The sign-in resource containing username and password.</param>
     * <returns>The authenticated user resource, including a JWT token</returns>
     */
    [HttpPost("sign-in")]
    [AllowAnonymous]
    [SwaggerOperation(
        Summary = "Sign in",
        Description = "Sign in a user",
        OperationId = "SignIn")] 
    [SwaggerResponse(StatusCodes.Status200OK, "The user was authenticated", typeof(AuthenticatedUserResource))]
    public async Task<IActionResult> SignIn([FromBody] SignInResource signInResource)
    {
        try
        {
            var signInCommand = SignInCommandFromResourceAssembler.ToCommandFromResource(signInResource);
            var authenticatedUser = await userCommandService.Handle(signInCommand);
            var resource =
                AuthenticatedUserResourceFromEntityAssembler.ToResourceFromEntity(authenticatedUser.user,
                    authenticatedUser.token);
            return Ok(resource);    
        }
        catch (InactiveAccountException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return Unauthorized(new
            {
                message = "An error occurred while sign-in in",
                error = ex.Message
            });
        } 
        
    }

    /**
     * <summary>
     *     Sign up endpoint. It allows creating a new user
     * </summary>
     * <param name="signUpResource">The sign-up resource containing username and password.</param>
     * <returns>A confirmation message on successful creation.</returns>
     */
    [HttpPost("sign-up")]
    [AllowAnonymous]
    [SwaggerOperation(
        Summary = "Sign-up",
        Description = "Sign up a new user",
        OperationId = "SignUp")]
    [SwaggerResponse(StatusCodes.Status201Created, "The user was created successfully", typeof(AuthenticatedUserResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Invalid data or email already taken")]
    public async Task<IActionResult> SignUp([FromBody] SignUpResource signUpResource)
    {
        try {
            var signUpCommand = SignUpCommandFromResourceAssembler.ToCommandFromResource(signUpResource);
            var authenticatedUser = await userCommandService.Handle(signUpCommand);
            var resource = AuthenticatedUserResourceFromEntityAssembler.ToResourceFromEntity(authenticatedUser.user,
                authenticatedUser.token);
            return CreatedAtAction(nameof(SignIn), resource);
        } catch (Exception ex)
        {
            return BadRequest(new
            {
                message = "An error occurred while creating the user.",
                error = ex.Message
            });
        }
    }
    
    [HttpPost("refresh-claims")]
    [SwaggerOperation(
        Summary = "Refresh Claims",
        Description = "Generates a new JWT with updated profile claims. " +
                      "Call this after completing your profile.",
        OperationId = "RefreshClaims")]
    [SwaggerResponse(StatusCodes.Status200OK, "New token generated", 
        typeof(RefreshedTokenResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Invalid or expired token")]
    public async Task<IActionResult> RefreshClaims()
    {
        try
        {
            var userId = User.GetUserId();

            var command = RefreshClaimsCommandFromResourceAssembler.ToCommandFromResource(userId);
            var newToken = await userCommandService.Handle(command);
            var resource = RefreshedTokenResourceFromTokenAssembler.ToResourceFromToken(newToken);
            return Ok(resource);
        }
        catch (InactiveAccountException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return Unauthorized(new
            {
                message = "Could not refresh claims",
                error = ex.Message
            });
        }
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [SwaggerOperation(
        Summary = "Request password reset",
        Description = "Sends a password reset token to the user's email. Returns 200 even if email not found (security).",
        OperationId = "RequestPasswordReset")]
    [SwaggerResponse(StatusCodes.Status200OK, "If the email exists, a reset token is generated.")]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Invalid data")]
    public async Task<IActionResult> ForgotPassword([FromBody] RequestPasswordResetResource resource)
    {
        try
        {
            var command = RequestPasswordResetCommandFromResourceAssembler.ToCommandFromResource(resource);
            await userCommandService.Handle(command);
            return Ok(new { message = "If the email exists, a reset link has been sent." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [SwaggerOperation(
        Summary = "Reset password with token",
        Description = "Resets the password using a valid reset token.",
        OperationId = "ResetPassword")]
    [SwaggerResponse(StatusCodes.Status200OK, "Password reset successfully")]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Invalid token or password policy violation")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordResource resource)
    {
        try
        {
            var command = ResetPasswordCommandFromResourceAssembler.ToCommandFromResource(resource);
            await userCommandService.Handle(command);
            return Ok(new { message = "Password reset successfully." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}