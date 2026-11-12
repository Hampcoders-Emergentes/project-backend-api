using System.Diagnostics;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.ErrorHandling;

public static class ErrorHandlingExtensions
{
    /// <summary>Registers the global exception handler, the MVC error result filter and RFC 7807 defaults.</summary>
    public static IServiceCollection AddUnifiedErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            // Also applied by MVC's ProblemDetailsFactory (automatic 400 validation responses).
            context.ProblemDetails.Instance ??= context.HttpContext.Request.Path.Value;
            context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.Configure<MvcOptions>(options => options.Filters.Add<ProblemDetailsResultFilter>());
        return services;
    }

    /// <summary>
    ///     Exception handler plus status code pages: any error response that would otherwise leave the pipeline with an
    ///     empty body (rate limiter 429, unmatched route 404, 405, empty 401/403...) gets the same JSON envelope.
    /// </summary>
    public static WebApplication UseUnifiedErrorHandling(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages(context => ApiProblemDetails.WriteAsync(context.HttpContext, context.HttpContext.Response.StatusCode));
        return app;
    }

    /// <summary>401/403 produced by the JWT bearer handler ([Microsoft.AspNetCore.Authorization.Authorize], Challenge(), Forbid()).</summary>
    public static JwtBearerEvents WithProblemDetailsResponses(this JwtBearerEvents events)
    {
        events.OnChallenge = async context =>
        {
            context.HandleResponse();
            if (context.Response.HasStarted) return;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            var detail = context.AuthenticateFailure switch
            {
                SecurityTokenExpiredException => "The access token has expired.",
                not null => "The access token is invalid.",
                _ => "A valid access token is required."
            };
            await ApiProblemDetails.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, detail);
        };
        events.OnForbidden = context =>
            ApiProblemDetails.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden);
        return events;
    }
}
