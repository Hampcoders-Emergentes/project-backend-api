using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Components;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Hampcoders.Electrolink.API.Processing.Infrastructure.Pipeline.Middleware.Attributes;

// Opt-in only on history. Human authorization by resource is a separate, pending policy.
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireRelayHistoryDeviceAccessAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.Features.Get<EdgeApiKeyAuthenticationFeature>() is null) return;

        var deviceId = context.RouteData.Values["deviceId"] as string;
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var allowed = !string.IsNullOrWhiteSpace(deviceId)
                      && config.GetSection("EdgeApi:RelayHistory:AllowedDeviceIds").GetChildren()
                          .Any(entry => string.Equals(entry.Value, deviceId, StringComparison.Ordinal));
        if (!allowed)
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
    }
}
