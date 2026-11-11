using System.Net.Mime;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes;
using Hampcoders.Electrolink.API.IAM.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.IAM.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Shared.Infrastructure.OpenApi;
using Microsoft.AspNetCore.Mvc;

namespace Hampcoders.Electrolink.API.IAM.Interfaces.REST;

/// <summary>
///     Password recovery by email. Same behaviour as the forgot/reset actions under /api/v1/authentication,
///     which remain for existing clients.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Produces(MediaTypeNames.Application.Json)]
[ApiDocTag("Password recovery endpoints")]
public sealed class PasswordRecoveryController(IUserCommandService users, ILogger<PasswordRecoveryController> logger) : ControllerBase
{
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ApiDocOperation(Summary = "Request a password reset link",
        Description = "Emails a single-use reset link valid for 15 minutes. Always returns 200, whether or not the email is registered.",
        OperationId = "AuthForgotPassword")]
    [ApiDocResponse(StatusCodes.Status200OK, "Request accepted")]
    public async Task<IActionResult> ForgotPassword([FromBody] RequestPasswordResetResource resource)
    {
        try { await users.Handle(RequestPasswordResetCommandFromResourceAssembler.ToCommandFromResource(resource)); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never reveal whether the account exists or whether delivery failed.
            logger.LogError(ex, "[IAM] forgot-password processing failed");
        }
        return Ok(new { message = "If the email exists, a reset link has been sent." });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ApiDocOperation(Summary = "Reset the password with a reset token",
        Description = "Validates the token (single use, 15 minutes), sets the new password and ends every active session.",
        OperationId = "AuthResetPassword")]
    [ApiDocResponse(StatusCodes.Status200OK, "Password reset")]
    [ApiDocResponse(StatusCodes.Status400BadRequest, "Invalid or expired token, or password policy violation")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordResource resource)
    {
        try
        {
            await users.Handle(ResetPasswordCommandFromResourceAssembler.ToCommandFromResource(resource));
            return Ok(new { message = "Password reset successfully." });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
