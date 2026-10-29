using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Planning.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Planning.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Hampcoders.Electrolink.API.Planning.Interfaces.REST;

// M3.30: the client is resolved server-side from the IAM user (active, concordant HomeOwner/Company profile), never from
// JWT profile claims. Ineligible actor → 403; another client's or a missing suggestion → generic 404; state conflict → 409.
[ApiController]
[Route("api/v1/suggestions")]
[Produces("application/json")]
public class ServiceSuggestionController(
    IServiceSuggestionQueryService queryService,
    IServiceSuggestionCommandService commandService,
    IServiceSuggestionRepository suggestionRepository,
    IUnitOfWork unitOfWork,
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

    private async Task<(ClientIdentity? Client, ServiceSuggestion? Suggestion, ActionResult? Denied)> ResolveOwnSuggestionAsync(string suggestionId)
    {
        var (client, denied) = await ResolveClientAsync();
        if (denied is not null) return (null, null, denied);
        var suggestion = await suggestionRepository.FindBySuggestionIdAsync(suggestionId);
        if (suggestion is null || suggestion.ClientId != client!.ClientId) return (null, null, ResourceNotFound());
        return (client, suggestion, null);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ServiceSuggestionResource>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ServiceSuggestionResource>>> GetActiveSuggestions()
    {
        var (client, denied) = await ResolveClientAsync();
        if (denied is not null) return denied;
        var suggestions = await queryService.GetActiveByClientIdAsync(client!.ClientId);
        return Ok(suggestions.Select(ServiceSuggestionResourceFromEntityAssembler.ToResource));
    }

    [HttpPatch("{suggestionId}/viewed")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarkAsViewed([FromRoute] string suggestionId)
    {
        var (_, suggestion, denied) = await ResolveOwnSuggestionAsync(suggestionId);
        if (denied is not null) return denied;
        try
        {
            suggestion!.MarkAsViewed();
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        await unitOfWork.CompleteAsync();

        return NoContent();
    }

    [HttpPost("{suggestionId}/accept")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AcceptSuggestion([FromRoute] string suggestionId)
    {
        var (client, _, denied) = await ResolveOwnSuggestionAsync(suggestionId);
        if (denied is not null) return denied;
        try
        {
            var requestId = await commandService.Handle(
                new AcceptServiceSuggestionCommand(suggestionId, client!));
            return Ok(new { requestId });
        }
        catch (ArgumentException) { return ResourceNotFound(); }
        catch (UnauthorizedAccessException) { return ResourceNotFound(); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPost("{suggestionId}/dismiss")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DismissSuggestion([FromRoute] string suggestionId)
    {
        var (client, _, denied) = await ResolveOwnSuggestionAsync(suggestionId);
        if (denied is not null) return denied;
        try
        {
            await commandService.Handle(
                new DismissServiceSuggestionCommand(suggestionId, client!));
            return NoContent();
        }
        catch (ArgumentException) { return ResourceNotFound(); }
        catch (UnauthorizedAccessException) { return ResourceNotFound(); }
    }
}
