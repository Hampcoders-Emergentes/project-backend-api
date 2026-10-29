using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Planning.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Interfaces.REST;
using Microsoft.AspNetCore.Mvc;
using HumanAuthorize = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AuthorizeAttribute;

namespace Hampcoders.Electrolink.API.Planning.Interfaces.REST;

[ApiController]
[Route("api/v1/service-requests")]
[Produces("application/json")]
public class ServiceRequestController(
    IServiceRequestCommandService commandService,
    IServiceDesignQueryService queryService)
    : ControllerBase
{
    [HttpGet("eligibility")]
    [ProducesResponseType(typeof(RequestEligibilityResource), StatusCodes.Status200OK)]
    public async Task<ActionResult<RequestEligibilityResource>> GetEligibility()
    {
        var client = User.GetClientIdentity();
        var (canCreate, planType, remaining, canPriority, reason) =
            await queryService.Handle(new GetRequestEligibilityQuery(client));

        return Ok(new RequestEligibilityResource(canCreate, planType, remaining, canPriority, reason));
    }

    [HttpPost]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Initiate()
    {
        try
        {
            var requestId = await commandService.Handle(
                new InitiateServiceRequestCommand(User.GetClientIdentity())) ?? throw new Exception("Failed to create service request");
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
        try
        {
            await commandService.Handle(new SelectPropertyForRequestCommand(
                RequestId.From(requestId), User.GetClientIdentity(), PropertyId.From(resource.PropertyId)));
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
        var results = await queryService.Handle(new GetAvailableServicesQuery(RequestId.From(requestId)));

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
        try
        {
            await commandService.Handle(new SelectServiceRecipeCommand(
                RequestId.From(requestId),
                User.GetClientIdentity(),
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
        try
        {
            await commandService.Handle(new AddServiceDetailsCommand(
                RequestId.From(requestId), User.GetClientIdentity(),
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
        try
        {
            var client = User.GetClientIdentity();
            await commandService.Handle(new ConfirmServiceRequestCommand(RequestId.From(requestId), client));

            var request = await queryService.Handle(
                new GetServiceRequestSummaryQuery(RequestId.From(requestId), client));

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
        try
        {
            await commandService.Handle(new CancelServiceRequestCommand(
                RequestId.From(requestId), User.GetClientIdentity(), resource.Reason, resource.Notes));
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
        var request = await queryService.Handle(
            new GetServiceRequestSummaryQuery(RequestId.From(requestId), User.GetClientIdentity()));

        if (request is null) return NotFound(new { message = "Request not found" });
        return Ok(ServiceRequestSummaryResourceFromEntityAssembler.ToResource(request));
    }
}