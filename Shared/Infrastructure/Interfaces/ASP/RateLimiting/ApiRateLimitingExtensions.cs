using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.ErrorHandling;
using Microsoft.AspNetCore.RateLimiting;

namespace Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.RateLimiting;

/// <summary>Bound from "RateLimiting". Fixed windows per caller (user id when authenticated, client IP otherwise).</summary>
public sealed class ApiRateLimitingSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Requests per window for an authenticated caller (partitioned by user id).</summary>
    public int AuthenticatedPermitLimit { get; set; } = 300;
    /// <summary>Requests per window for an anonymous caller (partitioned by client IP).</summary>
    public int AnonymousPermitLimit { get; set; } = 60;
    public int WindowSeconds { get; set; } = 60;
}

public static class ApiRateLimitingExtensions
{
    /// <summary>
    ///     Machine-to-machine routes with their own authentication and traffic profile: edge gateways stream telemetry
    ///     and Stripe retries webhooks, so a per-IP human limit would drop legitimate data.
    /// </summary>
    public static readonly IReadOnlyList<string> ExemptPathPrefixes = ["/api/v1/iot/edge", "/api/v1/alerts/dispatch", "/api/v1/webhooks"];

    /// <summary>
    ///     Global limiter for <c>/api/*</c> plus the rejection response shared by every policy (including the named
    ///     endpoint policies such as the contact form): HTTP 429, <c>Retry-After</c> in seconds and the RFC 7807 envelope
    ///     with <c>errorCode: "RATE_LIMITED"</c>.
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("RateLimiting").Get<ApiRateLimitingSettings>() ?? new ApiRateLimitingSettings();
        var window = TimeSpan.FromSeconds(Math.Clamp(settings.WindowSeconds, 1, 3600));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            if (settings.Enabled)
            {
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                {
                    if (!IsLimited(context.Request.Path)) return RateLimitPartition.GetNoLimiter("exempt");
                    var userId = context.User.Identity?.IsAuthenticated == true
                        ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")
                        : null;
                    return userId != null
                        ? RateLimitPartition.GetFixedWindowLimiter("user:" + userId, _ => Window(settings.AuthenticatedPermitLimit, window))
                        : RateLimitPartition.GetFixedWindowLimiter("ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                            _ => Window(settings.AnonymousPermitLimit, window));
                });
            }

            options.OnRejected = async (context, _) =>
            {
                var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? (int)Math.Ceiling(retryAfter.TotalSeconds)
                    : (int)window.TotalSeconds;
                await WriteRateLimitedAsync(context.HttpContext, seconds);
            };
        });
        return services;
    }

    /// <summary>Writes the 429 response (also used by controllers that enforce their own business quotas).</summary>
    public static Task WriteRateLimitedAsync(HttpContext context, int retryAfterSeconds, string? detail = null)
    {
        var seconds = Math.Max(1, retryAfterSeconds);
        context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        var problem = ApiProblemDetails.Create(context, StatusCodes.Status429TooManyRequests,
            detail ?? "Too many requests. Please try again later.", errorCode: ErrorCodes.RateLimited);
        problem.Extensions["retryAfterSeconds"] = seconds;
        return ApiProblemDetails.WriteAsync(context, problem);
    }

    private static bool IsLimited(PathString path) =>
        path.StartsWithSegments("/api") && !ExemptPathPrefixes.Any(prefix => path.StartsWithSegments(prefix));

    private static FixedWindowRateLimiterOptions Window(int permits, TimeSpan window) => new()
    {
        PermitLimit = Math.Max(1, permits), Window = window, QueueLimit = 0, AutoReplenishment = true
    };
}
