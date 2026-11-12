using Hampcoders.Electrolink.API.Shared.Interfaces.REST;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Microsoft.AspNetCore.Mvc;
using HumanAuthorize = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AuthorizeAttribute;
namespace Hampcoders.Electrolink.API.StoreManagement.Interfaces.REST;

[ApiController, HumanAuthorize]
[Route("api/v1/stores/{storeId}/dashboard-summary")]
[Produces("application/json")]
public sealed class StoreDashboardController(StoreDashboardService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<StoreDashboardSummaryResource>> Get(string storeId)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try { return Ok(await service.Handle(new GetStoreDashboardSummaryQuery(actor.Id, storeId), HttpContext.RequestAborted)); }
        catch (HierarchyNotFoundException notFound) { return NotFound(ErrorBodies.Coded("Resource not found.", notFound.ErrorCode)); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
}
