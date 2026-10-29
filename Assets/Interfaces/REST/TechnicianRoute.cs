using Hampcoders.Electrolink.API.Assets.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace Hampcoders.Electrolink.API.Assets.Interfaces.REST;

// Shared by technicians/{technicianId} routes (M3.24): the route technician must be the actor's own subject.
// Ineligible actor → 403; another technician → generic 404.
internal static class TechnicianRoute
{
    internal static async Task<(TechnicianId? Technician, ActionResult? Denied)> AuthorizeAsync(
        ControllerBase controller, IProfilesContextFacade profiles, string technicianId)
    {
        if (controller.HttpContext.Items["User"] is not User actor) return (null, controller.Unauthorized());
        var technician = await TechnicianActorResolver.ResolveAsync(profiles, actor);
        if (technician is null) return (null, controller.StatusCode(StatusCodes.Status403Forbidden));
        if (technician.Value != technicianId) return (null, PropertyOwnerRoute.NotFound(controller));
        return (technician, null);
    }
}
