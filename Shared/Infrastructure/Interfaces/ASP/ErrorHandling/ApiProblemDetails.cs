using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.ErrorHandling;

/// <summary>
///     Builds and writes the single RFC 7807 error envelope of the API:
///     <c>{ type, title, status, detail, instance, traceId, errors? }</c> with content type <c>application/problem+json</c>.
///     Every error path (MVC results, exception handler, status code pages, IAM middleware, JWT challenge/forbid) goes through it.
/// </summary>
public static class ApiProblemDetails
{
    public const string ContentType = "application/problem+json";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static ProblemDetails Create(HttpContext context, int status, string? detail = null, string? title = null,
        IDictionary<string, string[]>? errors = null)
    {
        var problem = errors is null
            ? new ProblemDetails()
            : new ValidationProblemDetails(errors);
        problem.Status = status;
        problem.Title = title ?? TitleFor(status);
        problem.Detail = string.IsNullOrWhiteSpace(detail) ? DefaultDetailFor(status) : detail;
        return Enrich(problem, context);
    }

    /// <summary>Fills the members every envelope must carry (type, title, instance, traceId) when they are missing.</summary>
    public static ProblemDetails Enrich(ProblemDetails problem, HttpContext context)
    {
        var status = problem.Status ?? context.Response.StatusCode;
        problem.Status = status;
        problem.Type ??= TypeFor(status);
        problem.Title ??= TitleFor(status);
        problem.Detail ??= DefaultDetailFor(status);
        problem.Instance ??= context.Request.Path.HasValue ? context.Request.Path.Value : "/";
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
        return problem;
    }

    public static Task WriteAsync(HttpContext context, int status, string? detail = null, string? title = null,
        IDictionary<string, string[]>? errors = null)
        => WriteAsync(context, Create(context, status, detail, title, errors));

    public static async Task WriteAsync(HttpContext context, ProblemDetails problem)
    {
        if (context.Response.HasStarted) return;
        Enrich(problem, context);
        context.Response.StatusCode = problem.Status!.Value;
        context.Response.ContentType = ContentType;
        // Serialize through the runtime type so ValidationProblemDetails keeps its "errors" member.
        await JsonSerializer.SerializeAsync(context.Response.Body, problem, problem.GetType(), SerializerOptions, context.RequestAborted);
    }

    public static string TitleFor(int status) => status switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        406 => "Not Acceptable",
        408 => "Request Timeout",
        409 => "Conflict",
        410 => "Gone",
        412 => "Precondition Failed",
        413 => "Content Too Large",
        415 => "Unsupported Media Type",
        422 => "Unprocessable Content",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        501 => "Not Implemented",
        502 => "Bad Gateway",
        503 => "Service Unavailable",
        504 => "Gateway Timeout",
        _ => status >= 500 ? "Server Error" : "Client Error"
    };

    public static string DefaultDetailFor(int status) => status switch
    {
        400 => "The request is invalid.",
        401 => "A valid access token is required.",
        403 => "You are not allowed to perform this action.",
        404 => "Resource not found.",
        405 => "The HTTP method is not allowed on this resource.",
        409 => "The request conflicts with the current state of the resource.",
        415 => "The request content type is not supported.",
        422 => "The request could not be processed.",
        429 => "Too many requests. Please try again later.",
        500 => "An unexpected error occurred.",
        502 => "An upstream provider failed.",
        503 => "The service is temporarily unavailable.",
        _ => TitleFor(status)
    };

    public static string TypeFor(int status) => status switch
    {
        400 => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        401 => "https://tools.ietf.org/html/rfc9110#section-15.5.2",
        403 => "https://tools.ietf.org/html/rfc9110#section-15.5.4",
        404 => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        405 => "https://tools.ietf.org/html/rfc9110#section-15.5.6",
        406 => "https://tools.ietf.org/html/rfc9110#section-15.5.7",
        408 => "https://tools.ietf.org/html/rfc9110#section-15.5.9",
        409 => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        410 => "https://tools.ietf.org/html/rfc9110#section-15.5.11",
        412 => "https://tools.ietf.org/html/rfc9110#section-15.5.13",
        413 => "https://tools.ietf.org/html/rfc9110#section-15.5.14",
        415 => "https://tools.ietf.org/html/rfc9110#section-15.5.16",
        422 => "https://tools.ietf.org/html/rfc9110#section-15.5.21",
        429 => "https://tools.ietf.org/html/rfc6585#section-4",
        500 => "https://tools.ietf.org/html/rfc9110#section-15.6.1",
        501 => "https://tools.ietf.org/html/rfc9110#section-15.6.2",
        502 => "https://tools.ietf.org/html/rfc9110#section-15.6.3",
        503 => "https://tools.ietf.org/html/rfc9110#section-15.6.4",
        504 => "https://tools.ietf.org/html/rfc9110#section-15.6.5",
        _ => "about:blank"
    };
}
