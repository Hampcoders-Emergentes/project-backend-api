using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;

public static class ComplianceMapping
{
    private const string Schema = "store_management";

    /// <summary>Compliance records live next to the store data they certify (same database, own schema).</summary>
    public static void MapCompliance(this ModelBuilder model)
    {
        var c = model.Entity<WeeklySafetyChecklist>();
        c.ToTable("weekly_safety_checklists", Schema); c.HasKey(x => x.Id);
        c.Property(x => x.Id).HasMaxLength(42);
        c.Property(x => x.StoreId).HasMaxLength(38).IsRequired();
        c.Property(x => x.InspectorStaffId).HasMaxLength(60).IsRequired();
        c.Property(x => x.InspectorName).HasMaxLength(120).IsRequired();
        c.Property(x => x.ItemsJson).HasColumnType("jsonb").IsRequired();
        c.Property(x => x.Status).HasMaxLength(20).IsRequired();
        c.Property(x => x.Observations).HasMaxLength(2000);
        c.Property(x => x.CreatedByUserId).HasMaxLength(100).IsRequired();
        c.Ignore(x => x.Items); c.Ignore(x => x.WeekLabel);
        c.HasIndex(x => new { x.StoreId, x.WeekStartDate });
        c.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);

        var m = model.Entity<EquipmentMaintenanceCertificate>();
        m.ToTable("maintenance_certificates", Schema); m.HasKey(x => x.Id);
        m.Property(x => x.Id).HasMaxLength(37);
        m.Property(x => x.StoreId).HasMaxLength(38).IsRequired();
        m.Property(x => x.EquipmentId).HasMaxLength(42).IsRequired();
        m.Property(x => x.TechnicianName).HasMaxLength(120).IsRequired();
        m.Property(x => x.CertificationNumber).HasMaxLength(60).IsRequired();
        m.Property(x => x.WorkDescription).HasMaxLength(1000);
        m.Property(x => x.DocumentFileUrl).HasMaxLength(500);
        m.Property(x => x.RegisteredByUserId).HasMaxLength(100).IsRequired();
        m.HasIndex(x => new { x.EquipmentId, x.CertificationNumber }).IsUnique();
        m.HasIndex(x => new { x.StoreId, x.ExpiresAtUtc });
        m.HasIndex(x => x.ExpiresAtUtc);
        m.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);
        m.HasOne<ElectricalEquipment>().WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);

        var s = model.Entity<ComplianceSignature>();
        s.ToTable("compliance_signatures", Schema); s.HasKey(x => x.Id);
        s.Property(x => x.Id).HasMaxLength(42);
        s.Property(x => x.StoreId).HasMaxLength(38).IsRequired();
        s.Property(x => x.SignerName).HasMaxLength(120).IsRequired();
        s.Property(x => x.SignerTitle).HasMaxLength(120).IsRequired();
        s.Property(x => x.ImagePng).IsRequired();
        s.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        s.Property(x => x.SignedByUserId).HasMaxLength(100).IsRequired();
        s.HasIndex(x => new { x.StoreId, x.SignedAtUtc });
        s.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);

        var r = model.Entity<OhsReport>();
        r.ToTable("ohs_reports", Schema); r.HasKey(x => x.Id);
        r.Property(x => x.Id).HasMaxLength(42);
        r.Property(x => x.StoreId).HasMaxLength(38).IsRequired();
        r.Property(x => x.RequestedByUserId).HasMaxLength(100).IsRequired();
        r.Property(x => x.FileName).HasMaxLength(200).IsRequired();
        r.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        r.Property(x => x.Content).IsRequired();
        r.Property(x => x.SignatureId).HasMaxLength(42);
        r.HasIndex(x => new { x.StoreId, x.GeneratedAtUtc });
        r.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ComplianceRepository(StoreManagementDbContext context) : IComplianceRepository
{
    public void Add(WeeklySafetyChecklist checklist) => context.Set<WeeklySafetyChecklist>().Add(checklist);
    public Task<WeeklySafetyChecklist?> FindChecklist(string storeId, string checklistId, CancellationToken ct) =>
        context.Set<WeeklySafetyChecklist>().SingleOrDefaultAsync(c => c.StoreId == storeId && c.Id == checklistId, ct);
    public Task<List<WeeklySafetyChecklist>> ListChecklists(string storeId, string? status, DateOnly? fromWeek, DateOnly? toWeek, int skip, int take,
        CancellationToken ct)
    {
        var query = context.Set<WeeklySafetyChecklist>().AsNoTracking().Where(c => c.StoreId == storeId);
        if (status != null) query = query.Where(c => c.Status == status);
        if (fromWeek != null) query = query.Where(c => c.WeekStartDate >= fromWeek.Value);
        if (toWeek != null) query = query.Where(c => c.WeekStartDate <= toWeek.Value);
        return query.OrderByDescending(c => c.WeekStartDate).ThenByDescending(c => c.UpdatedAtUtc).ThenBy(c => c.Id).Skip(skip).Take(take).ToListAsync(ct);
    }

    public void Add(EquipmentMaintenanceCertificate certificate) => context.Set<EquipmentMaintenanceCertificate>().Add(certificate);
    public Task<bool> CertificateNumberExists(string equipmentId, string certificationNumber, CancellationToken ct) =>
        context.Set<EquipmentMaintenanceCertificate>().AnyAsync(c => c.EquipmentId == equipmentId && c.CertificationNumber == certificationNumber, ct);
    public Task<List<EquipmentMaintenanceCertificate>> Certificates(string storeId, CancellationToken ct, string? equipmentId = null) =>
        context.Set<EquipmentMaintenanceCertificate>().AsNoTracking()
            .Where(c => c.StoreId == storeId && (equipmentId == null || c.EquipmentId == equipmentId))
            .OrderByDescending(c => c.ExpiresAtUtc).ThenBy(c => c.Id).ToListAsync(ct);
    public Task<List<EquipmentMaintenanceCertificate>> CertificatesExpiringBefore(DateTime limitUtc, int take, CancellationToken ct) =>
        context.Set<EquipmentMaintenanceCertificate>()
            .Where(c => c.ExpiresAtUtc <= limitUtc && (c.LastExpiryNoticeDays == null || c.LastExpiryNoticeDays > 0))
            .OrderBy(c => c.ExpiresAtUtc).Take(take).ToListAsync(ct);

    public void Add(ComplianceSignature signature) => context.Set<ComplianceSignature>().Add(signature);
    public Task<ComplianceSignature?> LatestSignature(string storeId, CancellationToken ct) =>
        context.Set<ComplianceSignature>().AsNoTracking().Where(s => s.StoreId == storeId)
            .OrderByDescending(s => s.SignedAtUtc).ThenBy(s => s.Id).FirstOrDefaultAsync(ct);

    public void Add(OhsReport report) => context.Set<OhsReport>().Add(report);
    public Task<OhsReport?> FindReport(string storeId, string reportId, CancellationToken ct) =>
        context.Set<OhsReport>().AsNoTracking().SingleOrDefaultAsync(r => r.StoreId == storeId && r.Id == reportId, ct);
    public async Task<List<OhsReport>> ListReports(string storeId, int take, CancellationToken ct)
    {
        // Project without the file content, then rebuild lightweight entities for the caller.
        var rows = await context.Set<OhsReport>().AsNoTracking().Where(r => r.StoreId == storeId)
            .OrderByDescending(r => r.GeneratedAtUtc).ThenBy(r => r.Id).Take(take)
            .Select(r => new { r.Id, r.StoreId, r.RequestedByUserId, r.FileName, r.ContentType, r.SizeBytes, r.FromUtc, r.ToUtc,
                r.GeneratedAtUtc, r.ComplianceScore, r.SignatureId }).ToListAsync(ct);
        return rows.Select(r => OhsReport.Restore(r.Id, r.StoreId, r.RequestedByUserId, r.FileName, r.ContentType, r.SizeBytes, r.FromUtc,
            r.ToUtc, r.GeneratedAtUtc, r.ComplianceScore, r.SignatureId)).ToList();
    }

    public Task Save(CancellationToken ct) => context.SaveChangesAsync(ct);
}
