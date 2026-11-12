using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Idempotency;

/// <summary>
///     Makes a POST endpoint safe to retry. When the request carries an <c>Idempotency-Key</c> header, the first successful
///     response (2xx) is stored for 24 hours under (caller, method + path, key) and returned again — same status, body and
///     <c>Location</c>, plus <c>Idempotent-Replayed: true</c> — without executing the command a second time.
///     <list type="bullet">
///         <item>Same key while the first request is still running → 409.</item>
///         <item>Same key with a different body or route values → 422.</item>
///         <item>Error responses are not stored, so a failed attempt can be retried with the same key.</item>
///         <item>Without the header the endpoint behaves as before.</item>
///     </list>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentEndpointAttribute : Attribute, IFilterFactory
{
    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        new IdempotencyFilter(serviceProvider.GetRequiredService<IdempotencyStore>());
}

internal sealed class IdempotencyFilter(IdempotencyStore store) : IAsyncActionFilter, IAsyncResultFilter
{
    private const int MaxKeyLength = 128;
    private static readonly JsonSerializerOptions FingerprintOptions = new(JsonSerializerDefaults.Web);

    private string? _scopedKey;
    private string? _fingerprint;
    private bool _replayed;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        var key = request.Headers[IdempotencyHeaders.Key].FirstOrDefault()?.Trim();
        if (!HttpMethods.IsPost(request.Method) || string.IsNullOrEmpty(key))
        {
            await next();
            return;
        }

        if (key.Length > MaxKeyLength)
        {
            context.Result = new BadRequestObjectResult(new { message = $"{IdempotencyHeaders.Key} must contain at most {MaxKeyLength} characters." });
            return;
        }

        var scopedKey = $"{CallerOf(context.HttpContext)}|{request.Method}|{request.Path.Value?.ToLowerInvariant()}|{key}";
        var fingerprint = Fingerprint(context);

        if (store.TryGet(scopedKey, out var stored) && stored is not null)
        {
            if (!string.Equals(stored.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                context.Result = Mismatch();
                return;
            }
            Replay(context, stored);
            return;
        }

        if (!store.TryBegin(scopedKey, fingerprint))
        {
            context.Result = store.IsInFlightWithOtherPayload(scopedKey, fingerprint)
                ? Mismatch()
                : new ConflictObjectResult(new { message = "A request with this Idempotency-Key is still being processed. Retry shortly." });
            return;
        }

        _scopedKey = scopedKey;
        _fingerprint = fingerprint;
        try
        {
            var executed = await next();
            // Exceptions and non-success results release the key without storing anything.
            if (executed.Exception is not null && !executed.ExceptionHandled) Release();
        }
        catch
        {
            Release();
            throw;
        }
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (_scopedKey is null || _replayed)
        {
            await next();
            return;
        }

        try
        {
            await next();
            var response = context.HttpContext.Response;
            IdempotentResponse? toStore = null;
            if (response.StatusCode is >= 200 and < 300)
            {
                var (value, declaredType) = context.Result is ObjectResult objectResult
                    ? (objectResult.Value, objectResult.DeclaredType)
                    : (null, null);
                toStore = new IdempotentResponse(response.StatusCode, value, declaredType, response.Headers.Location.FirstOrDefault(), _fingerprint!);
            }
            store.Complete(_scopedKey, toStore);
        }
        catch
        {
            store.Complete(_scopedKey, null);
            throw;
        }
        finally
        {
            _scopedKey = null;
        }
    }

    private void Release()
    {
        if (_scopedKey is null) return;
        store.Complete(_scopedKey, null);
        _scopedKey = null;
    }

    private void Replay(ActionExecutingContext context, IdempotentResponse stored)
    {
        _replayed = true;
        var response = context.HttpContext.Response;
        response.Headers[IdempotencyHeaders.Replayed] = "true";
        if (stored.Location is not null) response.Headers.Location = stored.Location;
        context.Result = stored.Value is null
            ? new StatusCodeResult(stored.StatusCode)
            : new ObjectResult(stored.Value) { StatusCode = stored.StatusCode, DeclaredType = stored.DeclaredType };
    }

    private static UnprocessableEntityObjectResult Mismatch() =>
        new(new { message = "This Idempotency-Key was already used with a different request payload." });

    /// <summary>Authenticated user id when there is one; anonymous callers (public forms) are scoped by client address.</summary>
    private static string CallerOf(HttpContext context)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId)) return "user:" + userId;
        if (context.Request.Headers.ContainsKey("X-Api-Key")) return "edge:" + context.Request.Headers["X-Api-Key"].ToString().GetHashCode(StringComparison.Ordinal);
        return "anon:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }

    /// <summary>Hash of the bound route, query and body arguments (injected services are never part of it).</summary>
    private static string Fingerprint(ActionExecutingContext context)
    {
        var arguments = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var parameter in context.ActionDescriptor.Parameters)
        {
            var source = parameter.BindingInfo?.BindingSource;
            var isRequestData = source == BindingSource.Body || source == BindingSource.Path || source == BindingSource.Query
                                || source == BindingSource.Form
                                || (source is null && (parameter.ParameterType.IsPrimitive || parameter.ParameterType == typeof(string)));
            if (isRequestData && context.ActionArguments.TryGetValue(parameter.Name, out var value)) arguments[parameter.Name] = value;
        }
        try
        {
            return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(arguments, FingerprintOptions)));
        }
        catch (NotSupportedException)
        {
            // Unserializable argument: fall back to the key alone (still prevents duplicate execution).
            return string.Empty;
        }
    }
}
