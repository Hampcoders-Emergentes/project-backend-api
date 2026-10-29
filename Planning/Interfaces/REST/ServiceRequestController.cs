using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Planning.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Microsoft.AspNetCore.Mvc;
using HumanAuthorize = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AuthorizeAttribute;

namespace Hampcoders.Electrolink.API.Planning.Interfaces.REST;

[ApiController]
[Route("api/v1/service-requests")]
[Produces("application/json")]
// M3.29: the client is resolved server-side from the IAM user (active, concordant HomeOwner/Company profile), never from
// JWT profile claims. Ineligible actor → 403; a request that is missing or belongs to another client → generic 404.
public class ServiceRequestController(
    IServiceRequestCommandService commandService,
    IServiceDesignQueryService queryService,
    ExternalProfilesService profiles)
    : ControllerBase
{
    private ObjectResult ResourceNotFound() => NotFound(new { message = "Resource not found." });

    private async Task<(ClientIdentity? Client, ActionResult? Denied)> ResolveClientAsync()
    {
        if (HttpContext.Items["User"] is not User actor) return (null, Unauthorized());
        if (actor.Status != EUserStatus.Active) return (null, StatusCode(StatusCodes.Status403Forbidden));
        var client = await profiles.GetActiveClientByUserIdAsync(actor.Id.Value);
        return client is null ? (null, StatusCode(StatusCodes.Status403Forbidden)) : (client, null);
    }

    private async Task<(ClientIdentity? Client, RequestId? Request, ActionResult? Denied)> ResolveOwnRequestAsync(string requestId)
    {
        var (client, denied) = await ResolveClientAsync();
        if (denied is not null) return (null, null, denied);
        if (!requestId.StartsWith("req-", StringComparison.Ordinal)) return (null, null, ResourceNotFound());
        var id = RequestId.From(requestId);
        if (await queryService.Handle(new GetServiceRequestSummaryQuery(id, client!)) is null) return (null, null, ResourceNotFound());
        return (client, id, null);
    }

    [HttpGet("eligibility")]
    [ProducesResponseType(typeof(RequestEligibilityResource), StatusCodes.Status200OK)]
    public async Task<ActionResult<RequestEligibilityResource>> GetEligibility()
    {
        var (client, denied) = await ResolveClientAsync();
        if (denied is not null) return denied;
        var (canCreate, planType, remaining, canPriority, reason) =
            await queryService.Handle(new GetRequestEligibilityQuery(client!));

        return Ok(new RequestEligibilityResource(canCreate, planType, remaining, canPriority, reason));
    }

    [HttpPost]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Initiate()
    {
        var (client, denied) = await ResolveClientAsync();
        if (denied is not null) return denied;
        try
        {
            var requestId = await commandService.Handle(
                new InitiateServiceRequestCommand(client!)) ?? throw new Exception("Failed to create service request");
            return StatusCode(StatusCodes.Status201Created, new { requestId = requestId.Value });
        }
        catch (RequestLimitReachedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpPost("{requestId}/property")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SelectProperty(
        [FromRoute] string requestId,
        [FromBody]  SelectPropertyResource resource)
    {
        var (client, request, denied) = await ResolveOwnRequestAsync(requestId);
        if (denied is not null) return denied;
        try
        {
            await commandService.Handle(new SelectPropertyForRequestCommand(
                request!, client!, PropertyId.From(resource.PropertyId)));
            return NoContent();
        }
        catch (InvalidRequestStatusException ex) { return BadRequest(new { message = ex.Message }); }
        catch (PropertyUnavailableForRequestException) { return NotFound(new { message = "Resource not found." }); }
    }

    [HttpGet("{requestId}/available-services")]
    [ProducesResponseType(typeof(IEnumerable<AvailableServiceResource>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<AvailableServiceResource>>> GetAvailableServices(
        [FromRoute] string requestId)
    {
        var (_, request, denied) = await ResolveOwnRequestAsync(requestId);
        if (denied is not null) return denied;
        var results = await queryService.Handle(new GetAvailableServicesQuery(request!));

        return Ok(results.Select(r => new AvailableServiceResource(
            r.ServiceCategory,
            r.CategoryDisplayName,
            r.MinPrice,
            r.MaxPrice,
            r.EstimatedDurationMinutes,
            r.TechniciansAvailable)));
    }

    [HttpPost("{requestId}/recipe")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SelectRecipe(
        [FromRoute] string requestId,
        [FromBody] SelectServiceRecipeResource recipeId)
    {
        var (client, request, denied) = await ResolveOwnRequestAsync(requestId);
        if (denied is not null) return denied;
        try
        {
            await commandService.Handle(new SelectServiceRecipeCommand(
                request!,
                client!,
                Enum.Parse<EServiceCategory>(recipeId.ServiceCategory, ignoreCase: true)));
            
            return NoContent();
        }
        catch (RecipeNotAvailableException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpPost("{requestId}/details")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddDetails(
        [FromRoute] string requestId,
        [FromBody]  AddServiceDetailsResource resource)
    {
        var (client, request, denied) = await ResolveOwnRequestAsync(requestId);
        if (denied is not null) return denied;
        try
        {
            await commandService.Handle(new AddServiceDetailsCommand(
                request!, client!,
                resource.ProblemDescription, resource.ConsumptionKwh,
                resource.AmountPaid, resource.AmountCurrency,
                resource.BillingPeriod, resource.ReceiptNumber,
                resource.IsPriority, resource.PreferredDates, resource.TimePreference));
            return NoContent();
        }
        catch (InvalidPreferredDateException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("{requestId}/confirm")]
    [ProducesResponseType(typeof(ServiceRequestSummaryResource), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Confirm([FromRoute] string requestId)
    {
        var (client, id, denied) = await ResolveOwnRequestAsync(requestId);
        if (denied is not null) return denied;
        try
        {
            await commandService.Handle(new ConfirmServiceRequestCommand(id!, client!));

            var request = await queryService.Handle(
                new GetServiceRequestSummaryQuery(id!, client!));

            if (request is null) return NotFound(new { message = "Request not found" });
            return AcceptedAtAction(nameof(GetSummary), new { requestId }, ServiceRequestSummaryResourceFromEntityAssembler.ToResource(request));
        }
        catch (RequestLimitReachedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpDelete("{requestId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(
        [FromRoute] string requestId,
        [FromBody]  CancelServiceRequestResource resource)
    {
        var (client, request, denied) = await ResolveOwnRequestAsync(requestId);
        if (denied is not null) return denied;
        try
        {
            await commandService.Handle(new CancelServiceRequestCommand(
                request!, client!, resource.Reason, resource.Notes));
            return NoContent();
        }
        catch (CannotCancelAssignedRequestException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPost("{requestId}/installation-property"), HumanAuthorize]
    [ProducesResponseType(typeof(InstallationPropertyAttachedResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AttachInstallationProperty([FromRoute] string requestId,
        [FromBody] AttachInstallationPropertyResource resource, [FromServices] SubscriptionInstallationPropertyService installations)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        if (actor.Status != EUserStatus.Active) return StatusCode(StatusCodes.Status403Forbidden);
        try
        {
            var (request, matching) = await installations.Handle(new AttachInstallationPropertyCommand(actor.Id, requestId, resource.PropertyId));
            return Ok(new InstallationPropertyAttachedResource(request.RequestId.Value, request.PropertyId!.Value, request.Status.ToString(), matching.ToString()));
        }
        catch (InstallationPropertyActorDeniedException) { return StatusCode(StatusCodes.Status403Forbidden); }
        catch (InstallationRequestUnavailableException) { return NotFound(new { message = "Resource not found." }); }
        catch (InstallationRequestConflictException ex) { return Conflict(new { message = ex.Message }); }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException ex)
            when (ex.Entries.Any(e => e.Entity is Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates.ServiceRequest r && r.RequestId.Value == requestId))
        { return Conflict(new { message = new InstallationRequestConflictException().Message }); }
    }

    [HttpGet("{requestId}")]
    [ProducesResponseType(typeof(ServiceRequestSummaryResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceRequestSummaryResource>> GetSummary(
        [FromRoute] string requestId)
    {
        var (client, id, denied) = await ResolveOwnRequestAsync(requestId);
        if (denied is not null) return denied;
        var request = await queryService.Handle(
            new GetServiceRequestSummaryQuery(id!, client!));

        if (request is null) return ResourceNotFound();
        return Ok(ServiceRequestSummaryResourceFromEntityAssembler.ToResource(request));
    }
}