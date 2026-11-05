using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Application.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hampcoders.Electrolink.API.Shared.Interfaces.REST;

// Same responses as the Assets and Planning ownership guards: ineligible actor → 403; another owner's or a missing resource → generic 404.
public static class ResourceAccess
{
    public static ObjectResult ResourceNotFound(this ControllerBase controller)
        => controller.NotFound(new { message = "Resource not found." });

    public static async Task<(ActorSubject? Subject, ActionResult? Denied)> ResolveSubjectAsync(
        this ControllerBase controller, IProfilesContextFacade profiles)
    {
        if (controller.HttpContext.Items["User"] is not User actor) return (null, controller.Unauthorized());
        var subject = await ActorSubjectResolver.ResolveAsync(profiles, actor.Id.Value);
        return subject is null ? (null, controller.StatusCode(StatusCodes.Status403Forbidden)) : (subject, null);
    }

    public static async Task<ActionResult?> DenyUnlessClientAsync(
        this ControllerBase controller, IProfilesContextFacade profiles, string clientId)
    {
        var (subject, denied) = await controller.ResolveSubjectAsync(profiles);
        if (denied is not null) return denied;
        return subject!.IsClient(clientId) ? null : controller.ResourceNotFound();
    }

    public static async Task<ActionResult?> DenyUnlessTechnicianAsync(
        this ControllerBase controller, IProfilesContextFacade profiles, string technicianId)
    {
        var (subject, denied) = await controller.ResolveSubjectAsync(profiles);
        if (denied is not null) return denied;
        return subject!.IsTechnician(technicianId) ? null : controller.ResourceNotFound();
    }
}
