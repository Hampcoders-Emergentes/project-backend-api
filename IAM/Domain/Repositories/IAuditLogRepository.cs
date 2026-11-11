using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;

namespace Hampcoders.Electrolink.API.IAM.Domain.Repositories;

/// <param name="OperatorId">User who performed the action.</param>
/// <param name="Resource">Case-insensitive fragment of the endpoint template (e.g. "alerts" or "assign").</param>
public sealed record AuditLogFilter(string StoreId, DateTime? FromUtc, DateTime? ToUtc, string? Action, string? Status,
    string? OperatorId = null, string? Resource = null);

public interface IAuditLogRepository
{
    Task AppendAsync(IReadOnlyCollection<AuditLogEntry> entries, CancellationToken ct);
    /// <summary>Newest first.</summary>
    Task<List<AuditLogEntry>> ListAsync(AuditLogFilter filter, int skip, int take, CancellationToken ct);
}
