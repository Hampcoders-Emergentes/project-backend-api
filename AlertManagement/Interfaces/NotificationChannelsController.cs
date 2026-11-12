using Hampcoders.Electrolink.API.AlertManagement.Application;
using Microsoft.AspNetCore.Mvc;
using HumanAuthorize = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AuthorizeAttribute;
namespace Hampcoders.Electrolink.API.AlertManagement.Interfaces;

/// <param name="Channel">EMAIL, SMS, DASHBOARD_PUSH or WEBHOOK.</param>
/// <param name="DeliveryMode">DELIVERED (a real provider sends it) or LOG_ONLY (only written to the server log).</param>
/// <param name="Configured">Whether the provider behind the channel is configured in this deployment.</param>
public sealed record NotificationChannelResponse(string Channel, string DeliveryMode, bool Configured);

public sealed record NotificationChannelsResponse(IReadOnlyList<NotificationChannelResponse> Items);

/// <summary>
///     Which alert channels actually reach people in this deployment, so the UI can warn before a manager routes alerts
///     to a channel that is only logged. Never exposes provider hosts, URLs or credentials.
/// </summary>
[ApiController, HumanAuthorize]
[Route("api/v1/notification-channels")]
[Produces("application/json")]
public sealed class NotificationChannelsController(NotificationChannelReadinessQueryService readiness) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(NotificationChannelsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<NotificationChannelsResponse> List()
    {
        Response.Headers.CacheControl = "private, max-age=60";
        return Ok(new NotificationChannelsResponse(readiness.Handle(new GetNotificationChannelReadinessQuery())
            .Select(c => new NotificationChannelResponse(c.Channel, c.DeliveryMode, c.Configured)).ToList()));
    }
}
