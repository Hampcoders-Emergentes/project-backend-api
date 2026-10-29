using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Services;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.ACL;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.REST.Transform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hampcoders.Electrolink.API.Subscriptions.Interfaces.REST;

[ApiController]
[Route("api/v1/subscriptions")]
// M3.31 (user decision): the ProfileId comes from the IAM user through Profiles, never from the JWT claim. Active and
// Suspended profiles (a failed payment suspends the profile) may use these endpoints; any other actor → 403.
public class SubscriptionsController(
    ISubscriptionCommandService commandService,
    ISubscriptionQueryService   queryService,
    ISubscriptionProfileResolver profiles) : ControllerBase
{
    private async Task<(string? ProfileId, ActionResult? Denied)> ResolveProfileAsync()
    {
        if (HttpContext.Items["User"] is not User actor) return (null, Unauthorized());
        if (actor.Status != EUserStatus.Active) return (null, StatusCode(StatusCodes.Status403Forbidden));
        var profileId = await profiles.ResolveSubscriptionProfileIdAsync(actor.Id.Value);
        return profileId is null ? (null, StatusCode(StatusCodes.Status403Forbidden)) : (profileId, null);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMySubscription()
    {
        var (profileId, denied) = await ResolveProfileAsync();
        if (denied is not null) return denied;
        var subscription = await queryService.Handle(new GetMySubscriptionQuery(profileId!));
        return Ok(MySubscriptionResourceFromEntityAssembler.ToResource(subscription));
    }

    [HttpGet("me/eligibility")]
    [Authorize]
    public async Task<IActionResult> GetRequestEligibility()
    {
        var (profileId, denied) = await ResolveProfileAsync();
        if (denied is not null) return denied;
        var result = await queryService.Handle(new GetRequestEligibilityQuery(profileId!));
        return Ok(RequestEligibilityResourceFromEntityAssembler.ToResource(result));
    }

    [HttpGet("me/payment-history")]
    [Authorize]
    public async Task<IActionResult> GetPaymentHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var (profileId, denied) = await ResolveProfileAsync();
        if (denied is not null) return denied;
        var records = await queryService.Handle(new GetPaymentHistoryQuery(profileId!, page, pageSize));
        return Ok(PaymentHistoryResourceFromEntityAssembler.ToResource(records, page, pageSize));
    }

    [HttpGet("me/status-alert")]
    [Authorize]
    public async Task<IActionResult> GetStatusAlert()
    {
        var (profileId, denied) = await ResolveProfileAsync();
        if (denied is not null) return denied;
        var subscription = await queryService.Handle(new GetSubscriptionStatusAlertQuery(profileId!));

        if (subscription is null) return NoContent();

        return Ok(SubscriptionStatusAlertResourceFromEntityAssembler.ToResource(subscription, null));
    }

    [HttpPost("checkout")]
    [Authorize]
    public async Task<IActionResult> InitiateCheckout([FromBody] InitiateCheckoutResource resource)
    {
        var (profileId, denied) = await ResolveProfileAsync();
        if (denied is not null) return denied;
        var command   = InitiateCheckoutCommandFromResourceAssembler.ToCommand(profileId!, resource);
        var result = await commandService.Handle(command);
        return Ok(new CheckoutUrlResource(result.CheckoutUrl, result.SessionId));
    }

    [HttpPost("me/cancel")]
    [Authorize]
    public async Task<IActionResult> CancelSubscription([FromBody] CancelSubscriptionResource resource)
    {
        var (profileId, denied) = await ResolveProfileAsync();
        if (denied is not null) return denied;
        var command   = CancelSubscriptionCommandFromResourceAssembler.ToCommand(profileId!, resource);
        var subscription = await commandService.Handle(command);
        return Ok(MySubscriptionResourceFromEntityAssembler.ToResource(subscription));
    }

    [HttpPost("me/portal")]
    [Authorize]
    public async Task<IActionResult> OpenCustomerPortal([FromBody] OpenCustomerPortalResource resource)
    {
        var (profileId, denied) = await ResolveProfileAsync();
        if (denied is not null) return denied;
        var command = new OpenCustomerPortalCommand(profileId!, resource.ReturnUrl);
        var result = await commandService.Handle(command);
        return Ok(new CustomerPortalUrlResource(result.PortalUrl));
    }
}
