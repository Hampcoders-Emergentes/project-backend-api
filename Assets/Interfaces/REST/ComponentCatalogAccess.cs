using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Microsoft.AspNetCore.Mvc;

namespace Hampcoders.Electrolink.API.Assets.Interfaces.REST;

// M3.25 (user decision): components and component types are a global catalogue without owner.
// Write: active Admin/SuperAdmin only (the {technicianId} route segment is not an owner for them).
// Read: those administrators, or the active technician on its own route (TechnicianRoute: 403 ineligible, 404 other technician).
internal static class ComponentCatalogAccess
{
    private static bool IsCatalogAdministrator(User actor)
        => actor.Status == EUserStatus.Active && actor.Role is AccessRole.Admin or AccessRole.SuperAdmin;

    internal static async Task<ActionResult?> AuthorizeReadAsync(ControllerBase controller, IProfilesContextFacade profiles, string technicianId)
    {
        if (controller.HttpContext.Items["User"] is not User actor) return controller.Unauthorized();
        if (IsCatalogAdministrator(actor)) return null;
        var (_, denied) = await TechnicianRoute.AuthorizeAsync(controller, profiles, technicianId);
        return denied;
    }

    internal static ActionResult? AuthorizeWrite(ControllerBase controller)
    {
        if (controller.HttpContext.Items["User"] is not User actor) return controller.Unauthorized();
        return IsCatalogAdministrator(actor) ? null : controller.StatusCode(StatusCodes.Status403Forbidden);
    }
}
