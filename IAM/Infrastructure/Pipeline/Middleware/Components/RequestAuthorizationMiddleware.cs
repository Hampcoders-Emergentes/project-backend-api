using Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Queries;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.ErrorHandling;

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
            if (EdgeApiKeyComparer.Matches(apiKey, configuredKey))
            {
                context.Features.Set(new EdgeApiKeyAuthenticationFeature());
                await next(context);
                return;
            }
        }

        var token = context.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        // Browsers cannot attach headers to WebSocket/SSE upgrades; SignalR sends the JWT as ?access_token=.
        if (string.IsNullOrEmpty(token) && context.Request.Path.StartsWithSegments("/hubs"))
            token = context.Request.Query["access_token"].FirstOrDefault();

        if (string.IsNullOrEmpty(token))
        {
            await ApiProblemDetails.WriteAsync(context, StatusCodes.Status401Unauthorized, "A valid access token is required.");
            return;
        }

        var userId = await tokenService.ValidateToken(token);

        if (string.IsNullOrEmpty(userId))
        {
            await ApiProblemDetails.WriteAsync(context, StatusCodes.Status401Unauthorized, "The access token is invalid or has expired.");
            return;
        }

        var user = await userQueryService.Handle(new GetUserByIdQuery(userId));

        if (user == null)
        {
            await ApiProblemDetails.WriteAsync(context, StatusCodes.Status401Unauthorized, "The account of this access token no longer exists.");
            return;
        }

        // M3.27: a valid token does not keep a Suspended/PendingVerification account usable.
        if (user.Status != EUserStatus.Active)
        {
            await ApiProblemDetails.WriteAsync(context, StatusCodes.Status403Forbidden, "The account is not active.",
                errorCode: Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions.ErrorCodes.AccountNotActive);
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
