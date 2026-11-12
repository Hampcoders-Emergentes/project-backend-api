using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Microsoft.EntityFrameworkCore;
namespace Hampcoders.Electrolink.API.AlertManagement.Infrastructure;

public static class AlertMapping
{
    public static void MapAlerts(this ModelBuilder model)
    {
        var a = model.Entity<Alert>();
        a.ToTable("alerts", "alert_management"); a.HasKey(x => x.Id);
        a.Property(x => x.Id).HasMaxLength(70);
        // Null for alerts reported by hand: they have no triggering reading.
        a.Property(x => x.BindingId).HasMaxLength(40); a.Property(x => x.ReadingId).HasMaxLength(100);
        a.Property(x => x.Source).HasMaxLength(20).IsRequired().HasDefaultValue(AlertSource.Telemetry);
        a.Property(x => x.ReportCategory).HasMaxLength(30); a.Property(x => x.Description).HasMaxLength(1000);
        a.Property(x => x.ReportedByWorkerCode).HasMaxLength(60); a.Property(x => x.ReportedByWorkerName).HasMaxLength(120);
        a.Property(x => x.OnDutyWorkerCode).HasMaxLength(60); a.Property(x => x.OnDutyWorkerName).HasMaxLength(120);
        a.Property(x => x.AssignedTechnicianId).HasMaxLength(100); a.Property(x => x.AssignedTechnicianName).HasMaxLength(120);
        a.Property(x => x.AssignedByUserId).HasMaxLength(100); a.Property(x => x.ResolvedByUserId).HasMaxLength(100);
        a.Property(x => x.ResolutionNotes).HasMaxLength(1000);
        a.Property(x => x.EquipmentId).HasMaxLength(42).IsRequired(); a.Property(x => x.OwnerUserId).HasMaxLength(100).IsRequired();
        a.Property(x => x.AnomalyType).HasMaxLength(80).IsRequired(); a.Property(x => x.DetectionSeverity).HasMaxLength(30).IsRequired();
        a.Property(x => x.Severity).HasMaxLength(30).IsRequired(); a.Property(x => x.Status).HasMaxLength(30).IsRequired();
        a.Property(x => x.AcknowledgedByUserId).HasMaxLength(100);
        a.HasIndex(x => new { x.BindingId, x.ReadingId }).IsUnique();
        a.HasIndex(x => new { x.EquipmentId, x.CreatedAtUtc });
        a.HasOne<EquipmentMeasurement>().WithMany().HasForeignKey(x => new { x.BindingId, x.ReadingId }).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<ElectricalEquipment>().WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);
        a.Ignore(x => x.IsCritical); a.Ignore(x => x.IsCriticalLeakage); a.Ignore(x => x.IsActive);
        a.HasIndex(x => new { x.Status, x.Severity, x.CreatedAtUtc });
        // Change stream position (GET .../alerts/changes?afterSequence=); 0 for rows last changed before sequencing.
        a.Property(x => x.Sequence).HasDefaultValue(0L);
        a.HasIndex(x => x.Sequence);

        var sequences = model.Entity<StoreAlertSequence>();
        sequences.ToTable(AlertSequencer.Table, AlertSequencer.Schema); sequences.HasKey(x => x.StoreId);
        sequences.Property(x => x.StoreId).HasColumnName("store_id").HasMaxLength(38);
        sequences.Property(x => x.LastSequence).HasColumnName("last_sequence");
        sequences.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);

        var d = model.Entity<AlertDispatch>();
        d.ToTable("alert_dispatches", "alert_management"); d.HasKey(x => x.Id);
        d.Property(x => x.Id).HasMaxLength(41);
        d.Property(x => x.AlertId).HasMaxLength(70).IsRequired(); d.Property(x => x.StoreId).HasMaxLength(38).IsRequired();
        d.Property(x => x.Channel).HasMaxLength(30).IsRequired(); d.Property(x => x.Destination).HasMaxLength(254).IsRequired();
        d.Property(x => x.Status).HasMaxLength(20).IsRequired(); d.Property(x => x.LastError).HasMaxLength(500);
        d.HasIndex(x => new { x.AlertId, x.Channel, x.Destination }).IsUnique();
        d.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
        d.HasOne<Alert>().WithMany().HasForeignKey(x => x.AlertId).OnDelete(DeleteBehavior.Restrict);
    }
}
