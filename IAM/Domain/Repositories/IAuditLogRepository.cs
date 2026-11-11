using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;

namespace Hampcoders.Electrolink.API.IAM.Domain.Repositories;

public sealed record AuditLogFilter(string StoreId, DateTime? FromUtc, DateTime? ToUtc, string? Action, string? Status);

public interface IAuditLogRepository
{
    Task AppendAsync(IReadOnlyCollection<AuditLogEntry> entries, CancellationToken ct);
    /// <summary>Newest first.</summary>
    Task<List<AuditLogEntry>> ListAsync(AuditLogFilter filter, int skip, int take, CancellationToken ct);
}
