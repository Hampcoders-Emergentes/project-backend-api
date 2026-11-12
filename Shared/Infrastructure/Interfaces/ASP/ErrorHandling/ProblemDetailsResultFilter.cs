using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.ErrorHandling;

/// <summary>
///     Rewrites every MVC error result (status &gt;= 400) into the unified <see cref="ApiProblemDetails"/> envelope, whatever
///     the controller returned: <c>{ message }</c>, <c>{ message, error }</c>, plain strings, typed resources or empty status
///     results. Runs for results produced by authorization filters too (IAlwaysRunResultFilter).
///     Members of a non-standard error body that are not the human-readable message are kept as extensions, so a body such
///     as the matching result (<c>assigned:false, failureReason</c>) loses no data. An <c>errorCode</c> member becomes the
///     envelope's code and an <c>errors</c> map (<c>IDictionary&lt;string, string[]&gt;</c>) its field errors.
/// </summary>
public sealed class ProblemDetailsResultFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        var http = context.HttpContext;
        switch (context.Result)
        {
            case ObjectResult { Value: ProblemDetails problem } objectResult when IsError(objectResult.StatusCode ?? problem.Status):
                problem.Status ??= objectResult.StatusCode;
                ApiProblemDetails.Enrich(problem, http);
                objectResult.StatusCode = problem.Status;
                SetProblemContentType(objectResult);
                break;

            case ObjectResult objectResult when IsError(objectResult.StatusCode):
                context.Result = ToProblemResult(http, objectResult.StatusCode!.Value, objectResult.Value);
                break;

            case IStatusCodeActionResult { StatusCode: { } status } and not ObjectResult and not FileResult when IsError(status):
                context.Result = ToProblemResult(http, status, null);
                break;
        }
    }

    public void OnResultExecuted(ResultExecutedContext context) { }

    private static bool IsError(int? status) => status is >= 400;

    private static ObjectResult ToProblemResult(HttpContext http, int status, object? value)
    {
        var (detail, extensions, errors) = Deconstruct(value);
        // A specific "errorCode" member of the controller body wins over the status default.
        var errorCode = extensions.Remove(ApiProblemDetails.ErrorCodeMember, out var code) ? code as string : null;
        var problem = ApiProblemDetails.Create(http, status, detail, errors: errors, errorCode: errorCode);
        foreach (var (key, extension) in extensions)
            problem.Extensions.TryAdd(key, extension);

        var result = new ObjectResult(problem) { StatusCode = status, DeclaredType = problem.GetType() };
        SetProblemContentType(result);
        return result;
    }

    private static void SetProblemContentType(ObjectResult result)
    {
        result.ContentTypes.Clear();
        result.ContentTypes.Add(ApiProblemDetails.ContentType);
        result.ContentTypes.Add("application/json");
    }

    private static (string? Detail, Dictionary<string, object?> Extensions, IDictionary<string, string[]>? Errors) Deconstruct(object? value)
    {
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal);
        switch (value)
        {
            case null:
                return (null, extensions, null);
            case string text:
                return (text, extensions, null);
            case Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary modelState:
                return ("One or more validation errors occurred.", extensions, ToErrors(modelState));
            case SerializableError serializable:
                return ("One or more validation errors occurred.", extensions,
                    serializable.ToDictionary(e => e.Key, e => e.Value as string[] ?? new[] { e.Value?.ToString() ?? string.Empty }));
        }

        // Anonymous objects and resources: lift the human-readable member into "detail", keep the others as extensions.
        var members = value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetIndexParameters().Length == 0)
            .ToDictionary(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name), p => p.GetValue(value), StringComparer.Ordinal);

        string? detail = null;
        // "error" carries the specific cause when a controller also sent a generic "message" (e.g. sign-in failures).
        foreach (var candidate in new[] { "error", "detail", "message", "failureReason" })
        {
            if (members.TryGetValue(candidate, out var text) && text is string { Length: > 0 } s)
            {
                detail = s;
                break;
            }
        }

        IDictionary<string, string[]>? errors = null;
        if (members.Remove("errors", out var fieldErrors) && fieldErrors is IDictionary<string, string[]> typedErrors)
            errors = typedErrors;
        else if (fieldErrors != null)
            members["errors"] = fieldErrors;

        foreach (var (key, member) in members)
        {
            // message/error/detail are folded into "detail"; failureReason stays unless it became the detail.
            if (member is string && (key is "message" or "error" or "detail" || Equals(member, detail))) continue;
            if (key is "type" or "title" or "status" or "instance" or "traceId") continue;
            extensions[key] = member;
        }
        return (detail, extensions, errors);
    }

    private static Dictionary<string, string[]> ToErrors(Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary modelState) =>
        modelState.Where(e => e.Value is { Errors.Count: > 0 })
            .ToDictionary(e => e.Key, e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray());
}
