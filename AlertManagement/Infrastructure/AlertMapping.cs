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
        a.Property(x => x.BindingId).HasMaxLength(40).IsRequired(); a.Property(x => x.ReadingId).HasMaxLength(100).IsRequired();
        a.Property(x => x.EquipmentId).HasMaxLength(42).IsRequired(); a.Property(x => x.OwnerUserId).HasMaxLength(100).IsRequired();
        a.Property(x => x.AnomalyType).HasMaxLength(80).IsRequired(); a.Property(x => x.DetectionSeverity).HasMaxLength(30).IsRequired();
        a.Property(x => x.Severity).HasMaxLength(30).IsRequired(); a.Property(x => x.Status).HasMaxLength(30).IsRequired();
        a.Property(x => x.AcknowledgedByUserId).HasMaxLength(100);
        a.HasIndex(x => new { x.BindingId, x.ReadingId }).IsUnique();
        a.HasIndex(x => new { x.EquipmentId, x.CreatedAtUtc });
        a.HasOne<EquipmentMeasurement>().WithMany().HasForeignKey(x => new { x.BindingId, x.ReadingId }).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<ElectricalEquipment>().WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
