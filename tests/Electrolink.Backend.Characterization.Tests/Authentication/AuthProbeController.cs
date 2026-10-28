using System.Security.Claims;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes;
using Hampcoders.Electrolink.API.Processing.Infrastructure.Pipeline.Middleware.Attributes;
using Microsoft.AspNetCore.Mvc;
using HumanAuthorize = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AuthorizeAttribute;

namespace Electrolink.Backend.Characterization.Tests.Authentication;

// Test-only endpoints isolate the actual filters; they are excluded from the Web project's compilation.
[ApiController]
[Route("__m02")]
public sealed class AuthProbeController : ControllerBase
{
    [HttpGet("plain")]
    public IActionResult Plain() => Snapshot();

    [HttpGet("human"), HumanAuthorize]
    public IActionResult Human() => Snapshot();

    [HttpGet("admin"), HumanAuthorize, RequireAccessRole(AccessRole.Admin, AccessRole.SuperAdmin)]
    public IActionResult Admin() => Snapshot();

    [HttpGet("edge"), RequireEdgeApiKey]
    public IActionResult Edge() => Snapshot();

    [HttpGet("edge-human"), RequireEdgeApiKey, HumanAuthorize]
    public IActionResult EdgeHuman() => Snapshot();

    [HttpGet("edge-admin"), RequireEdgeApiKey, RequireAccessRole(AccessRole.Admin)]
    public IActionResult EdgeAdmin() => Snapshot();

    [HttpGet("framework"), Microsoft.AspNetCore.Authorization.Authorize]
    public IActionResult Framework() => Snapshot();

    [HttpGet("edge-framework"), RequireEdgeApiKey, Microsoft.AspNetCore.Authorization.Authorize]
    public IActionResult EdgeFramework() => Snapshot();

    [HttpGet("anonymous"), AllowAnonymous, HumanAuthorize, RequireAccessRole(AccessRole.Admin)]
    public IActionResult Anonymous() => Snapshot();

    private IActionResult Snapshot() => Ok(new
    {
        authenticated = User.Identity?.IsAuthenticated == true,
        subject = User.FindFirstValue(ClaimTypes.NameIdentifier),
        itemUserId = (HttpContext.Items["User"] as User)?.Id.Value,
        itemRole = HttpContext.Items["AccessRole"] as string
    });
}
