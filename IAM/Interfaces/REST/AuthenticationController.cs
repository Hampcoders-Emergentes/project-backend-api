using System.Net.Mime;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Commands;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes;
using Hampcoders.Electrolink.API.IAM.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.IAM.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Interfaces.REST;
using Microsoft.AspNetCore.Mvc;
using Hampcoders.Electrolink.API.Shared.Infrastructure.OpenApi;

namespace Hampcoders.Electrolink.API.IAM.Interfaces.REST;

/// <summary>
///     Authentication, session refresh and password recovery. Canonical prefix: <c>/api/v1/auth</c>.
///     <see cref="LegacyAuthenticationController"/> keeps the former <c>/api/v1/authentication/*</c> routes working
///     (hidden from the OpenAPI document).
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/auth")]
[Produces(MediaTypeNames.Application.Json)]
[ApiDocTag("Available Authentication endpoints")]
public class AuthenticationController(IUserCommandService userCommandService, ILogger<AuthenticationController> logger) : ControllerBase
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
    [ApiDocOperation(
        Summary = "Sign in",
        Description = "Sign in a user",
        OperationId = "SignIn")] 
    [ApiDocResponse(StatusCodes.Status200OK, "The user was authenticated", typeof(AuthenticatedUserResource))]
    public async Task<IActionResult> SignIn([FromBody] SignInResource signInResource)
    {
        try
        {
            var signInCommand = SignInCommandFromResourceAssembler.ToCommandFromResource(signInResource);
            var authenticatedUser = await userCommandService.Handle(signInCommand);
            var resource =
                AuthenticatedUserResourceFromEntityAssembler.ToResourceFromEntity(authenticatedUser.user,
                    authenticatedUser.token, authenticatedUser.refreshToken);
            return Ok(resource);    
        }
        catch (InactiveAccountException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ErrorBodies.AccountNotActive());
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
    [ApiDocOperation(
        Summary = "Sign-up",
        Description = "Sign up a new user",
        OperationId = "SignUp")]
    [ApiDocResponse(StatusCodes.Status201Created, "The user was created successfully", typeof(AuthenticatedUserResource))]
    [ApiDocResponse(StatusCodes.Status400BadRequest, "Invalid data or email already taken")]
    public async Task<IActionResult> SignUp([FromBody] SignUpResource signUpResource)
    {
        try {
            var signUpCommand = SignUpCommandFromResourceAssembler.ToCommandFromResource(signUpResource);
            var authenticatedUser = await userCommandService.Handle(signUpCommand);
            var resource = AuthenticatedUserResourceFromEntityAssembler.ToResourceFromEntity(authenticatedUser.user,
                authenticatedUser.token, authenticatedUser.refreshToken);
            return CreatedAtAction(nameof(SignIn), resource);
        }
        // Stays 400 (not 409) for backward compatibility; clients branch on errorCode.
        catch (EmailAlreadyInUseException) { return BadRequest(ErrorBodies.EmailAlreadyRegistered()); }
        catch (InvalidEmailException) { return BadRequest(ErrorBodies.InvalidEmail()); }
        catch (FieldValidationException ex) { return BadRequest(ErrorBodies.Field(ex)); }
        catch (ArgumentException ex) { return BadRequest(ErrorBodies.Coded(ex.Message, ErrorCodes.ValidationFailed)); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unknown failures keep the historical 400 but never echo the internal message.
            logger.LogError(ex, "[IAM] sign-up failed");
            return BadRequest(ErrorBodies.Coded("An error occurred while creating the user.", ErrorCodes.RequestFailed));
        }
    }
    
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ApiDocOperation(
        Summary = "Refresh session",
        Description = "Exchanges a valid refresh token for a new access token and a rotated refresh token.",
        OperationId = "RefreshToken")]
    [ApiDocResponse(StatusCodes.Status200OK, "Session refreshed", typeof(AuthenticatedUserResource))]
    [ApiDocResponse(StatusCodes.Status401Unauthorized, "Invalid, expired or reused refresh token")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenResource resource)
    {
        try
        {
            var result = await userCommandService.Handle(new RefreshTokenCommand(resource.RefreshToken));
            return Ok(AuthenticatedUserResourceFromEntityAssembler.ToResourceFromEntity(result.user, result.token, result.refreshToken));
        }
        catch (InactiveAccountException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ErrorBodies.AccountNotActive());
        }
        catch (InvalidRefreshTokenException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpPost("refresh-claims")]
    [ApiDocOperation(
        Summary = "Refresh Claims",
        Description = "Generates a new JWT with updated profile claims. " +
                      "Call this after completing your profile.",
        OperationId = "RefreshClaims")]
    [ApiDocResponse(StatusCodes.Status200OK, "New token generated", 
        typeof(RefreshedTokenResource))]
    [ApiDocResponse(StatusCodes.Status401Unauthorized, "Invalid or expired token")]
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
        catch (InactiveAccountException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ErrorBodies.AccountNotActive());
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
    [ApiDocOperation(
        Summary = "Request password reset",
        Description = "Emails a single-use reset link valid for 15 minutes. Always returns 200, whether or not the email is registered.",
        OperationId = "RequestPasswordReset")]
    [ApiDocResponse(StatusCodes.Status200OK, "If the email exists, a reset token is generated.")]
    [ApiDocResponse(StatusCodes.Status400BadRequest, "Invalid data")]
    public async Task<IActionResult> ForgotPassword([FromBody] RequestPasswordResetResource resource)
    {
        try
        {
            var command = RequestPasswordResetCommandFromResourceAssembler.ToCommandFromResource(resource);
            await userCommandService.Handle(command);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never leak whether the account exists or whether delivery failed.
            logger.LogError(ex, "[IAM] forgot-password processing failed");
        }
        return Ok(new { message = "If the email exists, a reset link has been sent." });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ApiDocOperation(
        Summary = "Reset password with token",
        Description = "Resets the password using a valid reset token.",
        OperationId = "ResetPassword")]
    [ApiDocResponse(StatusCodes.Status200OK, "Password reset successfully")]
    [ApiDocResponse(StatusCodes.Status400BadRequest, "Invalid token or password policy violation")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordResource resource)
    {
        try
        {
            var command = ResetPasswordCommandFromResourceAssembler.ToCommandFromResource(resource);
            await userCommandService.Handle(command);
            return Ok(new { message = "Password reset successfully." });
        }
        catch (FieldValidationException ex)
        {
            return BadRequest(ErrorBodies.Field(ex));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

/// <summary>Backward-compatible alias of <see cref="AuthenticationController"/> under /api/v1/authentication. Do not use in new clients.</summary>
[Route("api/v1/authentication")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class LegacyAuthenticationController(IUserCommandService userCommandService, ILogger<AuthenticationController> logger)
    : AuthenticationController(userCommandService, logger);
