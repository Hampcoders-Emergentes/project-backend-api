using Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Application.Time;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.IAM.Application.Internal.QueryServices;

public sealed record GetStoreAuditLogsQuery(UserId Actor, string StoreId, string? From, string? To, string? Action, string? Status,
    int Page, int PageSize);

public sealed record AuditLogEntryResource(long Id, string? UserId, string? StoreId, string Action, string Resource, string HttpMethod,
    string Path, int StatusCode, string Status, string? IpAddress, DateTime TimestampUtc);

public sealed record AuditLogPageResource(IReadOnlyList<AuditLogEntryResource> Items, int Page, int PageSize, bool HasMore);

/// <summary>The store is unknown or not owned by the actor (indistinguishable on purpose).</summary>
public sealed class AuditStoreNotFoundException() : Exception("Store not found.");

public sealed class AuditLogQueryService(IAuditLogRepository repository, ExternalStoreAccessService stores)
{
    public async Task<AuditLogPageResource> Handle(GetStoreAuditLogsQuery query, CancellationToken ct)
    {
        if (query.Page < 1 || query.PageSize is < 1 or > 100 || (long)(query.Page - 1) * query.PageSize > int.MaxValue)
            throw new ArgumentException("page must be positive; pageSize must be between 1 and 100.");
        var action = query.Action?.Trim().ToUpperInvariant();
        var status = query.Status?.Trim().ToUpperInvariant();
        if (action != null && !AuditAction.All.Contains(action))
            throw new ArgumentException("action must be one of " + string.Join(", ", AuditAction.All) + ".");
        if (status != null && !AuditStatus.All.Contains(status))
            throw new ArgumentException("status must be one of " + string.Join(", ", AuditStatus.All) + ".");
        var interval = query.From == null && query.To == null ? null : ExplicitUtcInterval.Parse(query.From, query.To);
        if (!await stores.CanManage(query.Actor, query.StoreId, ct)) throw new AuditStoreNotFoundException();

        var rows = await repository.ListAsync(new AuditLogFilter(query.StoreId, interval?.FromUtc, interval?.ToUtc, action, status),
            (query.Page - 1) * query.PageSize, query.PageSize + 1, ct);
        return new(rows.Take(query.PageSize).Select(e => new AuditLogEntryResource(e.Id, e.UserId, e.StoreId, e.Action, e.Resource,
            e.HttpMethod, e.Path, e.StatusCode, e.Status, e.IpAddress, e.TimestampUtc)).ToList(), query.Page, query.PageSize,
            rows.Count > query.PageSize);
    }
}
