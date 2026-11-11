using Hampcoders.Electrolink.API.IAM.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Microsoft.AspNetCore.Mvc;
using HumanAuthorize = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AuthorizeAttribute;

namespace Hampcoders.Electrolink.API.IAM.Interfaces.REST;

/// <summary>Audit trail of a store, for its owner. Includes denied attempts made against the store by other accounts.</summary>
[ApiController, HumanAuthorize]
[Route("api/v1/stores/{storeId}/audit-logs")]
[Produces("application/json")]
public sealed class StoreAuditLogsController(AuditLogQueryService service) : ControllerBase
{
    /// <summary>
    ///     Newest first. Filters: from/to (ISO timestamps with offset, max 31 days), action=AUTHENTICATE|READ|CREATE|UPDATE|DELETE,
    ///     status=SUCCESS|FORBIDDEN|FAILED, operatorId (user who acted) and resource (fragment of the endpoint template, e.g.
    ///     "alerts" to trace who acknowledged, assigned or resolved each electrical event).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AuditLogPageResource>> List(string storeId, [FromQuery] string? from = null, [FromQuery] string? to = null,
        [FromQuery] string? action = null, [FromQuery] string? status = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        [FromQuery] string? operatorId = null, [FromQuery] string? resource = null)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try
        {
            return Ok(await service.Handle(new GetStoreAuditLogsQuery(actor.Id, storeId, from, to, action, status, page, pageSize, operatorId, resource),
                HttpContext.RequestAborted));
        }
        catch (AuditStoreNotFoundException) { return NotFound(new { message = "Resource not found." }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
}
