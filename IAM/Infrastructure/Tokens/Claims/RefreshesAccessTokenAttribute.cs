using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.IAM.Interfaces.REST.Transform;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Caching.Memory;

namespace Hampcoders.Electrolink.API.IAM.Infrastructure.Tokens.Claims;

public static class RefreshedTokenHeader
{
    /// <summary>Response header carrying a new access token whose claims reflect the change the request just made.</summary>
    public const string Name = "X-Refreshed-Token";
}

/// <summary>
///     For endpoints that change the caller's own profile, business role or subscription: after a successful (2xx) response
///     the caller's cached claims are evicted and a fresh access token is returned in <c>X-Refreshed-Token</c>. The client
///     replaces its stored access token with it (the refresh token is unchanged). A failure to issue the token never fails
///     the request itself.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RefreshesAccessTokenAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();
        if (executed.Exception is not null && !executed.ExceptionHandled) return;
        if (!IsSuccess(executed.Result)) return;
        if (context.HttpContext.Items["User"] is not User user) return;

        var services = context.HttpContext.RequestServices;
        CurrentClaimsTransformation.Invalidate(services.GetRequiredService<IMemoryCache>(), user.Id.Value);
        try
        {
            var token = await services.GetRequiredService<IUserCommandService>()
                .Handle(RefreshClaimsCommandFromResourceAssembler.ToCommandFromResource(user.Id.Value));
            context.HttpContext.Response.Headers[RefreshedTokenHeader.Name] = token;
        }
        catch (Exception ex) when (ex is InactiveAccountException or UserNotFoundException or ArgumentException)
        {
            services.GetRequiredService<ILogger<RefreshesAccessTokenAttribute>>()
                .LogWarning(ex, "[IAM] Could not issue a refreshed access token for {UserId}", user.Id.Value);
        }
    }

    private static bool IsSuccess(IActionResult? result) => result switch
    {
        IStatusCodeActionResult { StatusCode: { } status } => status is >= 200 and < 300,
        ObjectResult { StatusCode: null } => true,
        _ => false
    };
}

public static class CurrentClaimsServiceCollectionExtensions
{
    public static IServiceCollection AddCurrentClaimsRefresh(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<IClaimsTransformation, CurrentClaimsTransformation>();
        return services;
    }
}
