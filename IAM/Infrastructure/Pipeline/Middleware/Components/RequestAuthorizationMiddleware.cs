using Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Queries;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes;

namespace Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Components;

public class RequestAuthorizationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IUserQueryService userQueryService,
        ITokenService tokenService)
    {
        // GetEndpoint() puede ser null si el middleware corre antes del routing (ej. Swagger)
        var endpoint = context.GetEndpoint();
        var allowAnonymous = endpoint?.Metadata?.Any(m => m.GetType() == typeof(AllowAnonymousAttribute)) ?? false;

        if (allowAnonymous)
        {
            await next(context);
            return;
        }

        // Machine access must be explicit. Human filters/policies still need the loaded user,
        // even when the request also carries a valid Edge key.
        var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (AllowsEdgeApiKey(endpoint) && !string.IsNullOrEmpty(apiKey))
        {
            var config = context.RequestServices.GetRequiredService<IConfiguration>();
            var configuredKey = config["EdgeApi:ApiKey"];
            if (!string.IsNullOrEmpty(configuredKey) && apiKey == configuredKey)
            {
                context.Features.Set(new EdgeApiKeyAuthenticationFeature());
                await next(context);
                return;
            }
        }

        var token = context.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();

        if (string.IsNullOrEmpty(token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized: Missing token");
            return;
        }

        var userId = await tokenService.ValidateToken(token);

        if (string.IsNullOrEmpty(userId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized: Invalid token");
            return;
        }

        var user = await userQueryService.Handle(new GetUserByIdQuery(userId));

        if (user == null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized: User not found");
            return;
        }

        // M3.27: a valid token does not keep a Suspended/PendingVerification account usable.
        if (user.Status != EUserStatus.Active)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Forbidden: Account is not active");
            return;
        }

        context.Items["User"] = user;
        context.Items["AccessRole"] = user.Role.ToString();

        await next(context);
    }

    private static bool AllowsEdgeApiKey(Endpoint? endpoint)
    {
        if (endpoint is null) return false;
        var metadata = endpoint.Metadata;
        return metadata.GetMetadata<AllowEdgeApiKeyAttribute>() is not null
               && metadata.GetMetadata<AuthorizeAttribute>() is null
               && metadata.GetMetadata<RequireAccessRoleAttribute>() is null
               && metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>() is null;
    }

}
