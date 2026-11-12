using Hampcoders.Electrolink.API.Shared.Interfaces.REST;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Microsoft.AspNetCore.Mvc;
using HumanAuthorize = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AuthorizeAttribute;
namespace Hampcoders.Electrolink.API.StoreManagement.Interfaces.REST;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NotificationRouteInputResource(
    [Required, StringLength(30)] string Channel,
    [StringLength(254)] string? Destination,
    [Required, StringLength(30)] string RecipientRole,
    [StringLength(120)] string? RecipientName,
    [Required, MinLength(1), MaxLength(2)] IReadOnlyList<string> UrgencyLevels,
    bool Enabled = true);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateNotificationPreferencesResource([Required, MaxLength(20)] IReadOnlyList<NotificationRouteInputResource> Routes);

/// <summary>
///     Alert routes of a store. Channels: EMAIL (address), SMS (E.164 phone), DASHBOARD_PUSH (in-app, no destination),
///     WEBHOOK (public https URL receiving the alert as JSON).
///     Urgency levels: CRITICAL, NON_CRITICAL. PUT replaces the whole set; an empty set restores the default (in-app only).
/// </summary>
[ApiController, HumanAuthorize]
[Route("api/v1/stores/{storeId}/notification-preferences")]
[Produces("application/json")]
public sealed class StoreNotificationPreferencesController(StoreNotificationPreferenceService service) : ControllerBase
{
    [HttpGet]
    public Task<ActionResult<StoreNotificationPreferencesResource>> Get(string storeId) =>
        Execute(actor => service.Handle(new GetStoreNotificationPreferencesQuery(actor.Id, storeId), HttpContext.RequestAborted));

    [HttpPut]
    public Task<ActionResult<StoreNotificationPreferencesResource>> Put(string storeId, UpdateNotificationPreferencesResource body) =>
        Execute(actor => service.Handle(new UpdateStoreNotificationPreferencesCommand(actor.Id, storeId,
            body.Routes.Select(r => new NotificationRouteResource(r.Channel, r.Destination, r.RecipientRole, r.RecipientName,
                r.UrgencyLevels, r.Enabled)).ToList()), HttpContext.RequestAborted));

    private async Task<ActionResult<T>> Execute<T>(Func<User, Task<T>> action)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try { return Ok(await action(actor)); }
        catch (HierarchyNotFoundException notFound) { return NotFound(ErrorBodies.Coded("Resource not found.", notFound.ErrorCode)); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
}
