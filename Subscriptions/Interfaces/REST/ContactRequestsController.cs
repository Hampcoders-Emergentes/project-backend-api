using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Idempotency;
using Hampcoders.Electrolink.API.Shared.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Subscriptions.Infrastructure.Interfaces.ASP.Configuration.Extensions;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.REST.Resources;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hampcoders.Electrolink.API.Subscriptions.Interfaces.REST;

/// <summary>
///     Demo requests of the landing page. Submitting (rate limited per IP) and checking a receipt are public; the lead list
///     is for the back office (Admin / SuperAdmin).
/// </summary>
[ApiController]
[Route("api/v1/contact-requests")]
[Produces("application/json")]
public sealed class ContactRequestsController(ContactRequestCommandService service, ContactRequestQueryService queries) : ControllerBase
{
    /// <summary>Stores the request, notifies the sales team and emails a confirmation to the prospect.</summary>
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(WebApplicationBuilderExtensions.ContactFormRateLimit)]
    [IdempotentEndpoint]
    [ProducesResponseType(typeof(ContactRequestReceiptResource), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ContactRequestReceiptResource>> Submit(SubmitContactRequestResource body)
    {
        try
        {
            var receipt = await service.Handle(new SubmitContactRequestCommand(body.Name, body.Company, body.Email, body.Phone,
                body.Message, HttpContext.Connection.RemoteIpAddress?.ToString()), HttpContext.RequestAborted);
            return AcceptedAtAction(nameof(GetStatus), new { requestId = receipt.RequestId }, receipt);
        }
        catch (ContactRequestLimitException e)
        {
            // Rolling 24-hour quota per email: a full day is the upper bound before a slot frees up.
            Response.Headers.RetryAfter = "86400";
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message = e.Message,
                errorCode = Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions.ErrorCodes.RateLimited,
                retryAfterSeconds = 86400
            });
        }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }

    /// <summary>Receipt check by the id returned on submission: status and submission time only (no personal data).</summary>
    [HttpGet("{requestId}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ContactRequestStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ContactRequestStatusResponse>> GetStatus(string requestId)
    {
        var request = await queries.Handle(new GetContactRequestByIdQuery(requestId), HttpContext.RequestAborted);
        return request is null
            ? NotFound(new { message = "Resource not found." })
            : Ok(new ContactRequestStatusResponse(request.Id, request.Status, request.ReceivedAtUtc));
    }

    /// <summary>Demo leads, newest first. Filter: status=RECEIVED|NOTIFIED.</summary>
    [HttpGet]
    [Authorize]
    [RequireAccessRole(AccessRole.Admin, AccessRole.SuperAdmin)]
    [ProducesResponseType(typeof(PagedResponse<ContactRequestResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<ContactRequestResponse>>> List([FromQuery] PageRequest paging, [FromQuery] string? status = null)
    {
        try
        {
            var (items, total) = await queries.Handle(new ListContactRequestsQuery(status, paging.Skip, paging.PageSize), HttpContext.RequestAborted);
            return Ok(PagedResponse<ContactRequestResponse>.Create(items.Select(ToResponse).ToList(), paging, total));
        }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }

    private static ContactRequestResponse ToResponse(ContactRequest r) =>
        new(r.Id, r.Name, r.Company, r.Email, r.Phone, r.Message, r.Status, r.ReceivedAtUtc, r.NotifiedAtUtc);
}
