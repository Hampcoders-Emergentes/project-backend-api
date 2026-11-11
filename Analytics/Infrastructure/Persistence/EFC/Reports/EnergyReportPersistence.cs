using Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hampcoders.Electrolink.API.Analytics.Infrastructure.Persistence.EFC.Reports;

public static class EnergyReportMapping
{
    /// <summary>Report files live next to the store data they summarize (same database, own schema).</summary>
    public static void MapEnergyReports(this ModelBuilder model)
    {
        var r = model.Entity<EnergyReport>();
        r.ToTable("energy_reports", "analytics"); r.HasKey(x => x.Id);
        r.Property(x => x.Id).HasMaxLength(39);
        r.Property(x => x.StoreId).HasMaxLength(38).IsRequired();
        r.Property(x => x.RequestedByUserId).HasMaxLength(100).IsRequired();
        r.Property(x => x.Format).HasMaxLength(10).IsRequired();
        r.Property(x => x.FileName).HasMaxLength(200).IsRequired();
        r.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        r.Property(x => x.Content).IsRequired();
        r.HasIndex(x => new { x.StoreId, x.GeneratedAtUtc });
        r.HasIndex(x => x.ExpiresAtUtc);
        r.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Database-backed report storage: files are served by the download endpoint until they expire.</summary>
public sealed class DbEnergyReportStorage(StoreManagementDbContext context) : IEnergyReportStorage
{
    public async Task Store(EnergyReport report, CancellationToken ct)
    {
        context.Set<EnergyReport>().Add(report);
        await context.SaveChangesAsync(ct);
    }

    public Task<EnergyReport?> Find(string storeId, string reportId, CancellationToken ct) =>
        context.Set<EnergyReport>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == reportId && r.StoreId == storeId, ct);

    public Task<int> PurgeExpired(DateTime nowUtc, CancellationToken ct) =>
        context.Set<EnergyReport>().Where(r => r.ExpiresAtUtc <= nowUtc).ExecuteDeleteAsync(ct);
}
