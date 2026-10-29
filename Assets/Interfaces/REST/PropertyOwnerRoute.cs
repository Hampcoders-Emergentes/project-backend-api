using Hampcoders.Electrolink.API.Assets.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace Hampcoders.Electrolink.API.Assets.Interfaces.REST;

// Shared by the {homeowners|companies}/{ownerId} routes (M3.22, M3.23): the route owner must be the actor's own subject.
// Ineligible actor → 403; another owner or a route prefix that does not match the actor's role → generic 404.
internal static class PropertyOwnerRoute
{
    internal static ObjectResult NotFound(ControllerBase controller) => controller.NotFound(new { message = "Resource not found." });

    internal static async Task<(ClientIdentity? Owner, ActionResult? Denied)> AuthorizeAsync(
        ControllerBase controller, IProfilesContextFacade profiles, string ownerId)
    {
        if (controller.HttpContext.Items["User"] is not User actor) return (null, controller.Unauthorized());
        var owner = await PropertyOwnerResolver.ResolveAsync(profiles, actor);
        if (owner is null) return (null, controller.StatusCode(StatusCodes.Status403Forbidden));
        var routeType = controller.Request.Path.StartsWithSegments("/api/v1/companies", StringComparison.OrdinalIgnoreCase)
            ? EClientType.Company : EClientType.Homeowner;
        if (owner.ClientType != routeType || owner.ClientId != ownerId) return (null, NotFound(controller));
        return (owner, null);
    }
}
