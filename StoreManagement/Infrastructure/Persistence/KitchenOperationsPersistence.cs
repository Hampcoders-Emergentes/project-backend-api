using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;

public static class KitchenOperationsMapping
{
    private const string Schema = "store_management";

    public static void MapKitchenOperations(this ModelBuilder model)
    {
        var shifts = model.Entity<StaffShiftAssignment>();
        shifts.ToTable("staff_shift_assignments", Schema); shifts.HasKey(x => x.Id);
        shifts.Property(x => x.Id).HasMaxLength(38);
        shifts.Property(x => x.StoreId).HasMaxLength(38).IsRequired();
        shifts.Property(x => x.WorkerCode).HasMaxLength(60).IsRequired();
        shifts.Property(x => x.WorkerName).HasMaxLength(120).IsRequired();
        shifts.Property(x => x.Role).HasMaxLength(30).IsRequired();
        shifts.Property(x => x.CreatedByUserId).HasMaxLength(100).IsRequired();
        shifts.HasIndex(x => new { x.StoreId, x.StartsAtUtc, x.EndsAtUtc });
        shifts.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);

        var handovers = model.Entity<ShiftHandoverLog>();
        handovers.ToTable("shift_handover_logs", Schema); handovers.HasKey(x => x.Id);
        handovers.Property(x => x.Id).HasMaxLength(41);
        handovers.Property(x => x.StoreId).HasMaxLength(38).IsRequired();
        handovers.Property(x => x.WorkerId).HasMaxLength(60).IsRequired();
        handovers.Property(x => x.WorkerName).HasMaxLength(120).IsRequired();
        handovers.Property(x => x.EquipmentSnapshotsJson).HasColumnType("jsonb").IsRequired();
        handovers.Property(x => x.GridStatus).HasMaxLength(20).IsRequired();
        handovers.Property(x => x.Notes).HasMaxLength(2000);
        handovers.Property(x => x.RecordedByUserId).HasMaxLength(100).IsRequired();
        handovers.HasIndex(x => new { x.StoreId, x.RecordedAtUtc });
        handovers.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);

        var pauses = model.Entity<PanelPauseSession>();
        pauses.ToTable("panel_pause_sessions", Schema); pauses.HasKey(x => x.Id);
        pauses.Property(x => x.Id).HasMaxLength(38);
        pauses.Property(x => x.StoreId).HasMaxLength(38).IsRequired();
        pauses.Property(x => x.PanelId).HasMaxLength(100);
        pauses.Property(x => x.StartedByUserId).HasMaxLength(100).IsRequired();
        pauses.Property(x => x.EndReason).HasMaxLength(30);
        pauses.Property(x => x.OverridingAlertId).HasMaxLength(70);
        pauses.HasIndex(x => new { x.StoreId, x.StartedAtUtc });
        pauses.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);

        var protocols = model.Entity<EquipmentProtocol>();
        protocols.ToTable("equipment_protocols", Schema); protocols.HasKey(x => x.EquipmentId);
        protocols.Property(x => x.EquipmentId).HasMaxLength(42);
        protocols.Property(x => x.ShutdownSteps).HasColumnType("text[]").IsRequired();
        protocols.Property(x => x.EmergencyIsolationSteps).HasColumnType("text[]").IsRequired();
        protocols.Property(x => x.UpdatedByUserId).HasMaxLength(100);
        protocols.Ignore(x => x.IsDefault);
        protocols.OwnsOne(x => x.IsSafeToCleanRule, r =>
        {
            r.Property(x => x.MaxDeEnergizedCurrentAmps).HasColumnName("safe_to_clean_max_current_amps");
            r.Property(x => x.MaxReadingAgeMinutes).HasColumnName("safe_to_clean_max_reading_age_min");
            r.Property(x => x.RequireNoActiveCriticalAlerts).HasColumnName("safe_to_clean_requires_no_critical_alerts");
        });
        protocols.Navigation(x => x.IsSafeToCleanRule).IsRequired();
        protocols.HasOne<ElectricalEquipment>().WithOne().HasForeignKey<EquipmentProtocol>(x => x.EquipmentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class KitchenOperationsRepository(StoreManagementDbContext context) : IKitchenOperationsRepository
{
    public void Add(StaffShiftAssignment shift) => context.Set<StaffShiftAssignment>().Add(shift);
    public void Remove(StaffShiftAssignment shift) => context.Set<StaffShiftAssignment>().Remove(shift);
    public Task<StaffShiftAssignment?> FindShift(string storeId, string shiftId, CancellationToken ct) =>
        context.Set<StaffShiftAssignment>().SingleOrDefaultAsync(s => s.StoreId == storeId && s.Id == shiftId, ct);
    public Task<List<StaffShiftAssignment>> ShiftsOverlapping(string storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct) =>
        context.Set<StaffShiftAssignment>().AsNoTracking()
            .Where(s => s.StoreId == storeId && s.StartsAtUtc < toUtc && s.EndsAtUtc > fromUtc)
            .OrderBy(s => s.StartsAtUtc).ThenBy(s => s.Id).ToListAsync(ct);

    public void Add(ShiftHandoverLog handover) => context.Set<ShiftHandoverLog>().Add(handover);
    public Task<List<ShiftHandoverLog>> ListHandovers(string storeId, int skip, int take, CancellationToken ct) =>
        context.Set<ShiftHandoverLog>().AsNoTracking().Where(h => h.StoreId == storeId)
            .OrderByDescending(h => h.RecordedAtUtc).ThenBy(h => h.Id).Skip(skip).Take(take).ToListAsync(ct);

    public void Add(PanelPauseSession pause) => context.Set<PanelPauseSession>().Add(pause);
    public Task<PanelPauseSession?> LatestPause(string storeId, CancellationToken ct) =>
        context.Set<PanelPauseSession>().Where(p => p.StoreId == storeId)
            .OrderByDescending(p => p.StartedAtUtc).ThenBy(p => p.Id).FirstOrDefaultAsync(ct);

    public void Add(EquipmentProtocol protocol) => context.Set<EquipmentProtocol>().Add(protocol);
    public Task<EquipmentProtocol?> FindProtocol(string equipmentId, CancellationToken ct) =>
        context.Set<EquipmentProtocol>().SingleOrDefaultAsync(p => p.EquipmentId == equipmentId, ct);

    public Task<Store?> TrackedStore(UserId actor, string storeId, CancellationToken ct) =>
        context.Stores.SingleOrDefaultAsync(s => s.Id == storeId && s.OwnerUserId == actor, ct);
    public Task<List<PlacedEquipmentRow>> StoreEquipment(string storeId, CancellationToken ct) =>
        (from e in context.Equipment
         join a in context.Areas on e.AreaId equals a.Id
         where a.StoreId == storeId
         orderby a.Name, e.Name, e.Id
         select new PlacedEquipmentRow(e, a.Name)).ToListAsync(ct);

    public Task Save(CancellationToken ct) => context.SaveChangesAsync(ct);
}
