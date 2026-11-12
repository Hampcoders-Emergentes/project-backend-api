using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Repositories;
using Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hampcoders.Electrolink.API.IAM.Infrastructure.Auditing;

public static class AuditLogMapping
{
    /// <summary>Stored with the store data it refers to; no foreign keys, so entries outlive what they describe.</summary>
    public static void MapAuditLog(this ModelBuilder model)
    {
        var a = model.Entity<AuditLogEntry>();
        a.ToTable("audit_log_entries", "audit"); a.HasKey(x => x.Id);
        a.Property(x => x.Id).UseIdentityByDefaultColumn();
        a.Property(x => x.UserId).HasMaxLength(100); a.Property(x => x.StoreId).HasMaxLength(38);
        a.Property(x => x.Action).HasMaxLength(20).IsRequired(); a.Property(x => x.Resource).HasMaxLength(300).IsRequired();
        a.Property(x => x.HttpMethod).HasMaxLength(10).IsRequired(); a.Property(x => x.Path).HasMaxLength(500).IsRequired();
        a.Property(x => x.Status).HasMaxLength(20).IsRequired(); a.Property(x => x.IpAddress).HasMaxLength(45);
        a.HasIndex(x => new { x.StoreId, x.TimestampUtc });
        a.HasIndex(x => new { x.UserId, x.TimestampUtc });
    }
}

public sealed class AuditLogRepository(StoreManagementDbContext context) : IAuditLogRepository
{
    public async Task AppendAsync(IReadOnlyCollection<AuditLogEntry> entries, CancellationToken ct)
    {
        context.Set<AuditLogEntry>().AddRange(entries);
        await context.SaveChangesAsync(ct);
    }

    public Task<List<AuditLogEntry>> ListAsync(AuditLogFilter filter, int skip, int take, CancellationToken ct)
    {
        var query = context.Set<AuditLogEntry>().AsNoTracking().Where(e => e.StoreId == filter.StoreId);
        if (filter.FromUtc.HasValue) query = query.Where(e => e.TimestampUtc >= filter.FromUtc.Value);
        if (filter.ToUtc.HasValue) query = query.Where(e => e.TimestampUtc < filter.ToUtc.Value);
        if (filter.Action != null) query = query.Where(e => e.Action == filter.Action);
        if (filter.Status != null) query = query.Where(e => e.Status == filter.Status);
        if (filter.OperatorId != null) query = query.Where(e => e.UserId == filter.OperatorId);
        if (filter.Resource != null) query = query.Where(e => EF.Functions.ILike(e.Resource, "%" + filter.Resource + "%"));
        return query.OrderByDescending(e => e.TimestampUtc).ThenByDescending(e => e.Id).Skip(skip).Take(take).ToListAsync(ct);
    }
}
