using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Subscriptions.Infrastructure.Interfaces.ASP.Configuration.Extensions;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.REST.Resources;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using AllowAnonymous = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AllowAnonymousAttribute;

namespace Hampcoders.Electrolink.API.Subscriptions.Interfaces.REST;

/// <summary>Public demo request form of the landing page (no account required, rate limited per IP).</summary>
[ApiController, AllowAnonymous]
[Route("api/v1/contact-requests")]
[Produces("application/json")]
[EnableRateLimiting(WebApplicationBuilderExtensions.ContactFormRateLimit)]
public sealed class ContactRequestsController(ContactRequestCommandService service) : ControllerBase
{
    /// <summary>Stores the request, notifies the sales team and emails a confirmation to the prospect.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ContactRequestReceiptResource), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ContactRequestReceiptResource>> Submit(SubmitContactRequestResource body)
    {
        try
        {
            return Accepted(await service.Handle(new SubmitContactRequestCommand(body.Name, body.Company, body.Email, body.Phone,
                body.Message, HttpContext.Connection.RemoteIpAddress?.ToString()), HttpContext.RequestAborted));
        }
        catch (ContactRequestLimitException e) { return StatusCode(StatusCodes.Status429TooManyRequests, new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
}
