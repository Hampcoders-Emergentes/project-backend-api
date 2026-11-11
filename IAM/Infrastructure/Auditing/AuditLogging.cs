using System.Threading.Channels;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Repositories;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Components;

namespace Hampcoders.Electrolink.API.IAM.Infrastructure.Auditing;

/// <summary>In-memory buffer between the request pipeline and the database, so auditing never delays a response.</summary>
public sealed class AuditLogQueue
{
    private readonly Channel<AuditLogEntry> _channel = Channel.CreateBounded<AuditLogEntry>(
        new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    /// <summary>False when the buffer is full and the entry was dropped.</summary>
    public bool TryEnqueue(AuditLogEntry entry) => _channel.Writer.TryWrite(entry);
    public ChannelReader<AuditLogEntry> Reader => _channel.Reader;
}

/// <summary>Persists queued audit entries in batches.</summary>
public sealed class AuditLogWriter(AuditLogQueue queue, IServiceScopeFactory scopes, ILogger<AuditLogWriter> logger) : BackgroundService
{
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<AuditLogEntry>(BatchSize);
        while (await queue.Reader.WaitToReadAsync(stoppingToken))
        {
            while (batch.Count < BatchSize && queue.Reader.TryRead(out var entry)) batch.Add(entry);
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IAuditLogRepository>().AppendAsync(batch, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "[Audit] Could not persist {Count} audit entries", batch.Count);
            }
            batch.Clear();
        }
    }
}

/// <summary>
///     Records authenticated actions and access attempts. Logged: every non-GET request, every 401/403 response,
///     every authentication endpoint call and report downloads. Not logged: successful machine (edge API key) traffic,
///     successful reads (unless Audit:IncludeReads is true), CORS preflights, health, docs and realtime hub traffic.
///     Must run before the IAM request authorization middleware so its 401/403 responses are seen.
/// </summary>
public sealed class AuditLoggingMiddleware(RequestDelegate next, AuditLogQueue queue, IConfiguration configuration,
    ILogger<AuditLoggingMiddleware> logger)
{
    private static readonly string[] IgnoredPrefixes = ["/hubs", "/health", "/openapi", "/scalar"];
    private static readonly string[] AuthenticationPrefixes = ["/api/v1/authentication", "/api/v1/auth"];

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        var path = context.Request.Path;
        if (HttpMethods.IsOptions(method) || HttpMethods.IsHead(method) || IgnoredPrefixes.Any(p => path.StartsWithSegments(p)))
        {
            await next(context);
            return;
        }
        var threw = false;
        try { await next(context); }
        catch { threw = true; throw; }
        finally { Capture(context, threw); }
    }

    private void Capture(HttpContext context, bool threw)
    {
        try
        {
            var request = context.Request;
            // An unhandled exception leaves the default 200 on the response object while the client gets a 500.
            var code = threw && !context.Response.HasStarted ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
            var isAuthentication = AuthenticationPrefixes.Any(p => request.Path.StartsWithSegments(p));
            var denied = code is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden;
            var isMachine = context.Features.Get<EdgeApiKeyAuthenticationFeature>() != null;
            var isRead = HttpMethods.IsGet(request.Method);
            var sensitiveRead = request.Path.Value?.EndsWith("/download", StringComparison.OrdinalIgnoreCase) == true;
            var includeReads = configuration.GetValue("Audit:IncludeReads", false);
            if (!denied && !isAuthentication && (isMachine || (isRead && !includeReads && !sensitiveRead))) return;

            var status = code < 400 ? AuditStatus.Success
                : isAuthentication ? AuditStatus.Failed
                : denied ? AuditStatus.Forbidden : AuditStatus.Failed;
            var action = isAuthentication ? AuditAction.Authenticate : request.Method.ToUpperInvariant() switch
            {
                "GET" => AuditAction.Read, "POST" => AuditAction.Create, "PUT" or "PATCH" => AuditAction.Update,
                "DELETE" => AuditAction.Delete, _ => AuditAction.Read
            };
            var endpoint = context.GetEndpoint() as RouteEndpoint;
            var storeId = request.RouteValues.TryGetValue("storeId", out var value) && value is string s
                && s.StartsWith("store-", StringComparison.Ordinal) && s.Length <= 38 ? s : null;
            var user = context.Items["User"] as User;
            var entry = AuditLogEntry.Record(user?.Id.Value, storeId, action, endpoint?.RoutePattern.RawText ?? request.Path.Value ?? "/",
                request.Method, request.Path.Value ?? "/", code, status, context.Connection.RemoteIpAddress?.ToString(), DateTime.UtcNow);
            if (!queue.TryEnqueue(entry)) logger.LogWarning("[Audit] Buffer full; entry for {Path} dropped", request.Path.Value);
        }
        catch (Exception ex)
        {
            // Auditing must never break a request.
            logger.LogError(ex, "[Audit] Could not capture audit entry");
        }
    }
}

public static class AuditLoggingExtensions
{
    public static IApplicationBuilder UseAuditLogging(this IApplicationBuilder app) => app.UseMiddleware<AuditLoggingMiddleware>();
}
