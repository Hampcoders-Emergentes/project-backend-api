using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.AlertManagement.Infrastructure;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Microsoft.EntityFrameworkCore;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;

// Separate additive model: never runs EnsureCreated/Migrate automatically.
public sealed class StoreManagementDbContext(DbContextOptions<StoreManagementDbContext> options) : DbContext(options)
{
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StoreArea> Areas => Set<StoreArea>();
    public DbSet<ElectricalEquipment> Equipment => Set<ElectricalEquipment>();

    public DbSet<EquipmentDeviceBinding> DeviceBindings => Set<EquipmentDeviceBinding>();
    public DbSet<EquipmentMeasurement> Measurements => Set<EquipmentMeasurement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.MapAlerts();
        modelBuilder.HasDefaultSchema("store_management");
        var stores = modelBuilder.Entity<Store>();
        stores.ToTable("stores");
        stores.HasKey(s => s.Id);
        stores.Property(s => s.Id).HasMaxLength(38);
        stores.Property(s => s.Name).HasMaxLength(120).IsRequired();
        stores.Property(s => s.Address).HasMaxLength(300).IsRequired();
        stores.Property(s => s.OwnerUserId).HasConversion(id => id.Value, value => UserId.From(value)).HasMaxLength(100).IsRequired();
        stores.HasIndex(s => new { s.OwnerUserId, s.Id });
        var areas = modelBuilder.Entity<StoreArea>();
        areas.ToTable("areas");
        areas.HasKey(a => a.Id);
        areas.Property(a => a.Id).HasMaxLength(37);
        areas.Property(a => a.StoreId).HasMaxLength(38).IsRequired();
        areas.Property(a => a.Name).HasMaxLength(120).IsRequired();
        areas.HasOne<Store>().WithMany().HasForeignKey(a => a.StoreId).OnDelete(DeleteBehavior.Restrict);
        var equipment = modelBuilder.Entity<ElectricalEquipment>();
        equipment.ToTable("equipment");
        equipment.HasKey(e => e.Id);
        equipment.Property(e => e.Id).HasMaxLength(42);
        equipment.Property(e => e.AreaId).HasMaxLength(37).IsRequired();
        equipment.Property(e => e.Name).HasMaxLength(120).IsRequired();
        equipment.HasOne<StoreArea>().WithMany().HasForeignKey(e => e.AreaId).OnDelete(DeleteBehavior.Restrict);
        var bindings = modelBuilder.Entity<EquipmentDeviceBinding>();
        bindings.ToTable("device_bindings");
        bindings.HasKey(b => b.Id);
        bindings.Property(b => b.Id).HasMaxLength(40);
        bindings.Property(b => b.EquipmentId).HasMaxLength(42).IsRequired();
        bindings.Property(b => b.OwnerUserId).HasConversion(id => id.Value, value => UserId.From(value)).HasMaxLength(100).IsRequired();
        bindings.Property(b => b.DeviceId).HasMaxLength(100).IsRequired();
        bindings.Property(b => b.SerialNumber).HasMaxLength(200).IsRequired();
        bindings.Property(b => b.EdgeDeviceId).HasMaxLength(200).IsRequired();
        bindings.Property(b => b.LegacyPropertyId).HasMaxLength(100).IsRequired();
        bindings.Property(b => b.AuthorizedProfileId).HasMaxLength(100).IsRequired();
        bindings.Property(b => b.ThresholdProvenance).HasMaxLength(100).IsRequired();
        bindings.HasIndex(b => b.EquipmentId).IsUnique();
        bindings.HasIndex(b => b.DeviceId).IsUnique();
        bindings.HasIndex(b => b.EdgeDeviceId).IsUnique();
        bindings.HasOne<ElectricalEquipment>().WithMany().HasForeignKey(b => b.EquipmentId).OnDelete(DeleteBehavior.Restrict);
        bindings.OwnsOne(b => b.Thresholds, t =>
        {
            t.Property(x => x.NominalVoltage); t.Property(x => x.MaxConsumptionWatts);
            t.Property(x => x.MaxCurrentAmps); t.Property(x => x.MinPowerFactor);
            t.Property(x => x.NominalFrequency); t.Property(x => x.DisconnectionThresholdMin);
            t.Property(x => x.NormalLimitAmps); t.Property(x => x.AlertLimitAmps);
        });
        bindings.Navigation(b => b.Thresholds).IsRequired();
        var measurements = modelBuilder.Entity<EquipmentMeasurement>();
        measurements.ToTable("measurements");
        measurements.HasKey(r => new { r.BindingId, r.ReadingId });
        measurements.Property(r => r.BindingId).HasMaxLength(40);
        measurements.Property(r => r.ReadingId).HasMaxLength(100);
        measurements.Property(r => r.Source).HasMaxLength(30).IsRequired();
        measurements.Property(r => r.TimestampBasis).HasMaxLength(30).IsRequired();
        measurements.Property(r => r.AnomalyType).HasMaxLength(80);
        measurements.Property(r => r.Severity).HasMaxLength(30);
        measurements.HasIndex(r => new { r.BindingId, r.CapturedAtUtc });
        measurements.HasOne<EquipmentDeviceBinding>().WithMany().HasForeignKey(r => r.BindingId).OnDelete(DeleteBehavior.Restrict);
    }
}
