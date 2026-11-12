namespace Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;

public static class AuditStatus
{
    public const string Success = "SUCCESS";
    /// <summary>The caller was not authenticated or not allowed (401/403).</summary>
    public const string Forbidden = "FORBIDDEN";
    /// <summary>The action was allowed but did not succeed (validation error, not found, failed sign-in, server error).</summary>
    public const string Failed = "FAILED";
    public static readonly IReadOnlyList<string> All = [Success, Forbidden, Failed];
}

public static class AuditAction
{
    public const string Authenticate = "AUTHENTICATE";
    public const string Read = "READ";
    public const string Create = "CREATE";
    public const string Update = "UPDATE";
    public const string Delete = "DELETE";
    public static readonly IReadOnlyList<string> All = [Authenticate, Read, Create, Update, Delete];
}

/// <summary>Immutable record of one authenticated action or access attempt.</summary>
public sealed class AuditLogEntry
{
    public long Id { get; private set; }
    public string? UserId { get; private set; }
    public string? StoreId { get; private set; }
    public string Action { get; private set; } = null!;
    /// <summary>Route template of the endpoint (e.g. api/v1/stores/{storeId}/alerts), or the path when unrouted.</summary>
    public string Resource { get; private set; } = null!;
    public string HttpMethod { get; private set; } = null!;
    public string Path { get; private set; } = null!;
    public int StatusCode { get; private set; }
    public string Status { get; private set; } = null!;
    public string? IpAddress { get; private set; }
    public DateTime TimestampUtc { get; private set; }
    private AuditLogEntry() { }

    public static AuditLogEntry Record(string? userId, string? storeId, string action, string resource, string httpMethod, string path,
        int statusCode, string status, string? ipAddress, DateTime timestampUtc)
    {
        if (!AuditAction.All.Contains(action)) throw new ArgumentException("Unknown audit action.");
        if (!AuditStatus.All.Contains(status)) throw new ArgumentException("Unknown audit status.");
        return new()
        {
            UserId = Clip(userId, 100), StoreId = Clip(storeId, 38), Action = action, Resource = Clip(resource, 300)!,
            HttpMethod = Clip(httpMethod, 10)!, Path = Clip(path, 500)!, StatusCode = statusCode, Status = status,
            IpAddress = Clip(ipAddress, 45), TimestampUtc = timestampUtc
        };
    }

    private static string? Clip(string? value, int max) => value == null ? null : value.Length <= max ? value : value[..max];
}
