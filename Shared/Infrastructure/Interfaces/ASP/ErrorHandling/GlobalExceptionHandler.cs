using Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.ErrorHandling;

/// <summary>
///     Last-resort mapping of exceptions no controller handled into the unified error envelope. Only exception types whose
///     meaning is unambiguous are mapped to 4xx; anything else is a 500 whose body never exposes internals.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // The client went away: nothing to write.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested) return true;

        var (status, detail, errorCode) = exception switch
        {
            BadHttpRequestException bad => (bad.StatusCode, bad.Message, (string?)null),
            KeyNotFoundException e => (StatusCodes.Status404NotFound, e.Message, null),
            InvalidIdException e => (StatusCodes.Status400BadRequest, e.Message, null),
            ArgumentException e => (StatusCodes.Status400BadRequest, e.Message, null),
            // Missing or foreign identity claims (ClaimsPrincipalExtensions) and domain access checks.
            UnauthorizedAccessException e => (StatusCodes.Status403Forbidden, e.Message, null),
            Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException =>
                (StatusCodes.Status409Conflict, "The resource was modified by another request. Reload it and try again.", ErrorCodes.StaleState),
            _ => (StatusCodes.Status500InternalServerError, (string?)null, null)
        };
        // A coded 4xx exception names its own code; 5xx keep the generic one.
        if (status < 500 && exception is ICodedError coded) errorCode = coded.ErrorCode;

        if (status >= 500)
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        else
            logger.LogInformation("Mapped {ExceptionType} to {Status} on {Method} {Path}", exception.GetType().Name, status,
                httpContext.Request.Method, httpContext.Request.Path);

        await ApiProblemDetails.WriteAsync(httpContext, status, detail, errorCode: errorCode);
        return true;
    }
}
