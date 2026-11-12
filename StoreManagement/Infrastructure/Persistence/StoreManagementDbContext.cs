using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.AlertManagement.Infrastructure;
using Hampcoders.Electrolink.API.Analytics.Infrastructure.Persistence.EFC.Reports;
using Hampcoders.Electrolink.API.Analytics.Infrastructure.Persistence.EFC.Planning;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Auditing;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Microsoft.EntityFrameworkCore;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;

// Store-centric model (stores, telemetry, alerts, notification routes, kitchen operations, energy planning, compliance,
// audit trail); schema from its EF migrations.
public sealed class StoreManagementDbContext(DbContextOptions<StoreManagementDbContext> options) : DbContext(options)
{
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StoreArea> Areas => Set<StoreArea>();
    public DbSet<ElectricalEquipment> Equipment => Set<ElectricalEquipment>();

    public DbSet<EquipmentDeviceBinding> DeviceBindings => Set<EquipmentDeviceBinding>();
    public DbSet<EquipmentMeasurement> Measurements => Set<EquipmentMeasurement>();
    public DbSet<StoreNotificationPreference> NotificationPreferences => Set<StoreNotificationPreference>();
    public DbSet<AlertDispatch> AlertDispatches => Set<AlertDispatch>();
    public DbSet<StoreAlertSequence> AlertSequences => Set<StoreAlertSequence>();

    /// <summary>
    ///     Every alert creation or state change gets its store's next change sequence in the same transaction
    ///     (see <see cref="AlertSequencer"/>). Joins the caller's transaction when there is one.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var alerts = AlertSequencer.PendingChanges(ChangeTracker);
        if (alerts.Count == 0) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (Database.CurrentTransaction != null)
        {
            await AlertSequencer.StampAsync(this, alerts, cancellationToken);
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        await AlertSequencer.StampAsync(this, alerts, cancellationToken);
        var written = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return written;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        SaveChangesAsync(acceptAllChangesOnSuccess).GetAwaiter().GetResult();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.MapAlerts();
        modelBuilder.MapEnergyReports();
        modelBuilder.MapAuditLog();
        modelBuilder.MapKitchenOperations();
        modelBuilder.MapEnergyPlanning();
        modelBuilder.MapCompliance();
        modelBuilder.HasDefaultSchema("store_management");
        var stores = modelBuilder.Entity<Store>();
        stores.ToTable("stores");
        stores.HasKey(s => s.Id);
        stores.Property(s => s.Id).HasMaxLength(38);
        stores.Property(s => s.Name).HasMaxLength(120).IsRequired();
        stores.Property(s => s.Address).HasMaxLength(300).IsRequired();
        stores.Property(s => s.OwnerUserId).HasConversion(id => id.Value, value => UserId.From(value)).HasMaxLength(100).IsRequired();
        stores.HasIndex(s => new { s.OwnerUserId, s.Id });
        stores.Property(s => s.LayoutWidth).HasDefaultValue(Store.DefaultLayoutWidth);
        stores.Property(s => s.LayoutHeight).HasDefaultValue(Store.DefaultLayoutHeight);
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
        equipment.OwnsOne(e => e.Thresholds, t =>
        {
            t.Property(x => x.NominalVoltage); t.Property(x => x.MaxConsumptionWatts);
            t.Property(x => x.MaxCurrentAmps); t.Property(x => x.MinPowerFactor);
            t.Property(x => x.NominalFrequency); t.Property(x => x.DisconnectionThresholdMin);
            t.Property(x => x.NormalLimitAmps); t.Property(x => x.AlertLimitAmps);
        });
        equipment.Navigation(e => e.Thresholds).IsRequired();
        equipment.Ignore(e => e.Placement);
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
        bindings.HasIndex(b => b.EquipmentId).IsUnique();
        bindings.HasIndex(b => b.DeviceId).IsUnique();
        bindings.HasIndex(b => b.EdgeDeviceId).IsUnique();
        bindings.HasOne<ElectricalEquipment>().WithMany().HasForeignKey(b => b.EquipmentId).OnDelete(DeleteBehavior.Restrict);
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
        var preferences = modelBuilder.Entity<StoreNotificationPreference>();
        preferences.ToTable("notification_preferences");
        preferences.HasKey(p => p.Id);
        preferences.Property(p => p.Id).HasMaxLength(42);
        preferences.Property(p => p.StoreId).HasMaxLength(38).IsRequired();
        preferences.Property(p => p.Channel).HasMaxLength(30).IsRequired();
        preferences.Property(p => p.Destination).HasMaxLength(254);
        preferences.Property(p => p.RecipientRole).HasMaxLength(30).IsRequired();
        preferences.Property(p => p.RecipientName).HasMaxLength(120);
        preferences.Property(p => p.UrgencyLevels).HasMaxLength(60).IsRequired();
        preferences.Ignore(p => p.Urgencies);
        preferences.HasIndex(p => p.StoreId);
        preferences.HasOne<Store>().WithMany().HasForeignKey(p => p.StoreId).OnDelete(DeleteBehavior.Cascade);
    }
}
