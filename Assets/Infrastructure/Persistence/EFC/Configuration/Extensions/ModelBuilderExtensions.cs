using System.Text.Json;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Entities;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Hampcoders.Electrolink.API.Assets.Infrastructure.Persistence.EFC.Configuration.Extensions;

public static class ModelBuilderExtensions
{
    public static void ApplyAssetsConfiguration(this ModelBuilder builder)
    {
        ConfigureTechnicianInventory(builder);
        ConfigureComponentStock(builder);
        ConfigureComponentReservation(builder);
        ConfigureReservationItem(builder);
        ConfigureComponentType(builder);
        ConfigureComponent(builder);
        ConfigurePropertyPortfolio(builder);
        ConfigurePortfolioEntry(builder);
        ConfigureProperty(builder);
        ConfigureIoTDevice(builder);
    }

    private static void ConfigureTechnicianInventory(ModelBuilder builder)
    {
        builder.Entity<TechnicianInventory>().HasKey(ti => ti.Id);
        builder.Entity<TechnicianInventory>()
            .Property(ti => ti.Id)
            .HasConversion(id => id.Value, value => TechnicianInventoryId.From(value))
            .HasColumnName("Id")
            .IsRequired()
            .ValueGeneratedNever();

        builder.Entity<TechnicianInventory>()
            .Property(ti => ti.TechnicianId)
            .HasConversion(id => id.Value, value => TechnicianId.From(value))
            .HasColumnName("TechnicianId")
            .IsRequired();

        builder.Entity<TechnicianInventory>()
            .Property(ti => ti.Status)
            .HasConversion<string>()
            .HasColumnName("Status")
            .IsRequired();

        builder.Entity<TechnicianInventory>()
            .HasMany(ti => ti.StockItems)
            .WithOne()
            .HasForeignKey("TechnicianInventoryId")
            .IsRequired();

        builder.Entity<TechnicianInventory>()
            .HasMany(ti => ti.Reservations)
            .WithOne()
            .HasForeignKey("TechnicianInventoryId")
            .IsRequired();
    }

    private static void ConfigureComponentStock(ModelBuilder builder)
    {
        builder.Entity<ComponentStock>().HasKey(cs => cs.Id);
        builder.Entity<ComponentStock>()
            .Property(cs => cs.Id)
            .HasConversion(id => id.Value, value => ComponentStockId.From(value))
            .HasColumnName("Id")
            .IsRequired()
            .ValueGeneratedNever();

        builder.Entity<ComponentStock>()
            .Property(cs => cs.TechnicianInventoryId)
            .HasConversion(id => id.Value, value => TechnicianInventoryId.From(value))
            .HasColumnName("TechnicianInventoryId")
            .IsRequired();

        builder.Entity<ComponentStock>()
            .Property(cs => cs.ComponentId)
            .HasConversion(id => id.Value, value => ComponentId.From(value))
            .HasColumnName("ComponentId")
            .IsRequired();

        builder.Entity<ComponentStock>()
            .Property(cs => cs.ComponentTypeId)
            .HasConversion(id => id.Value, value => ComponentTypeId.From(value))
            .HasColumnName("ComponentTypeId")
            .IsRequired();
    }

    private static void ConfigureComponentReservation(ModelBuilder builder)
    {
        builder.Entity<ComponentReservation>().HasKey(cr => cr.Id);
        builder.Entity<ComponentReservation>()
            .Property(cr => cr.Id)
            .HasConversion(id => id.Value, value => ComponentReservationId.From(value))
            .HasColumnName("Id")
            .IsRequired()
            .ValueGeneratedNever();

        builder.Entity<ComponentReservation>()
            .Property(cr => cr.TechnicianInventoryId)
            .HasConversion(id => id.Value, value => TechnicianInventoryId.From(value))
            .HasColumnName("TechnicianInventoryId")
            .IsRequired();

        builder.Entity<ComponentReservation>()
            .Property(cr => cr.AssignmentId)
            .HasConversion(id => id.Value, value => AssignmentId.From(value))
            .HasColumnName("ServiceId")
            .IsRequired();

        builder.Entity<ComponentReservation>()
            .HasMany(cr => cr.Items)
            .WithOne()
            .HasForeignKey("ReservationId")
            .IsRequired();
    }

    private static void ConfigureReservationItem(ModelBuilder builder)
    {
        builder.Entity<ReservationItem>().HasKey(ri => ri.Id);
        builder.Entity<ReservationItem>()
            .Property(ri => ri.Id)
            .HasConversion(id => id.Value, value => ReservationItemId.From(value))
            .HasColumnName("Id")
            .IsRequired()
            .ValueGeneratedNever();

        builder.Entity<ReservationItem>()
            .Property(ri => ri.ReservationId)
            .HasConversion(id => id.Value, value => ComponentReservationId.From(value))
            .HasColumnName("ReservationId")
            .IsRequired();

        builder.Entity<ReservationItem>()
            .Property(ri => ri.ComponentId)
            .HasConversion(id => id.Value, value => ComponentId.From(value))
            .HasColumnName("ComponentId")
            .IsRequired();

        builder.Entity<ReservationItem>()
            .Property(cs => cs.ComponentTypeId)
            .HasConversion(id => id.Value, value => ComponentTypeId.From(value))
            .HasColumnName("ComponentTypeId")
            .IsRequired();
    }

    private static void ConfigureComponentType(ModelBuilder builder)
    {
        builder.Entity<ComponentType>().HasKey(ct => ct.Id);
        builder.Entity<ComponentType>()
            .Property(ct => ct.Id)
            .HasConversion(id => id.Value, value => ComponentTypeId.From(value))
            .HasColumnName("Id")
            .IsRequired()
            .ValueGeneratedNever();

        builder.Entity<ComponentType>().Property(ct => ct.Name).IsRequired();
    }

    private static void ConfigureComponent(ModelBuilder builder)
    {
        builder.Entity<Component>().HasKey(c => c.Id);
        builder.Entity<Component>()
            .Property(c => c.Id)
            .HasConversion(id => id.Value, value => ComponentId.From(value))
            .HasColumnName("Id")
            .IsRequired()
            .ValueGeneratedNever();

        builder.Entity<Component>().HasIndex(c => c.Name).IsUnique();
        builder.Entity<Component>().HasIndex(c => c.IsActive);
    }

    private static void ConfigurePropertyPortfolio(ModelBuilder builder)
    {
        builder.Entity<PropertyPortfolio>().HasKey(pp => pp.Id);
        builder.Entity<PropertyPortfolio>()
            .Property(pp => pp.Id)
            .HasConversion(id => id.Value, value => PropertyPortfolioId.From(value))
            .HasColumnName("Id")
            .IsRequired()
            .ValueGeneratedNever();

        builder.Entity<PropertyPortfolio>()
            .OwnsOne(pp => pp.Owner, owner =>
            {
                owner.WithOwner().HasForeignKey("Id");
                owner.Property(o => o.ClientType)
                    .HasConversion<string>()
                    .HasColumnName("owner_type")
                    .HasMaxLength(20)
                    .IsRequired();
                owner.Property(o => o.ClientId)
                    .HasColumnName("owner_id")
                    .HasMaxLength(100)
                    .IsRequired();
            });

        builder.Entity<PropertyPortfolio>()
            .Property(pp => pp.Status)
            .HasConversion<string>()
            .HasColumnName("Status")
            .IsRequired();

        builder.Entity<PropertyPortfolio>()
            .HasMany(pp => pp.Entries)
            .WithOne()
            .HasForeignKey("PortfolioId")
            .IsRequired();
    }

    private static void ConfigurePortfolioEntry(ModelBuilder builder)
    {
        builder.Entity<PortfolioEntry>().HasKey(pe => pe.Id);
        builder.Entity<PortfolioEntry>()
            .Property(pe => pe.Id)
            .HasConversion(id => id.Value, value => PortfolioEntryId.From(value))
            .HasColumnName("Id")
            .IsRequired()
            .ValueGeneratedNever();

        builder.Entity<PortfolioEntry>()
            .Property(pe => pe.PortfolioId)
            .HasConversion(id => id.Value, value => PropertyPortfolioId.From(value))
            .HasColumnName("PortfolioId")
            .IsRequired();

        builder.Entity<PortfolioEntry>()
            .Property(pe => pe.PropertyId)
            .HasConversion(id => id.Value, value => PropertyId.From(value))
            .HasColumnName("PropertyId")
            .IsRequired();

        builder.Entity<PortfolioEntry>()
            .Property(pe => pe.OccupancyStatus)
            .HasConversion<string>()
            .IsRequired();
    }

    private static void ConfigureProperty(ModelBuilder builder)
    {
        builder.Entity<Property>().HasKey(prop => prop.Id);
        builder.Entity<Property>()
            .Property(p => p.Id)
            .HasConversion(id => id.Value, value => PropertyId.From(value))
            .HasColumnName("Id")
            .IsRequired()
            .ValueGeneratedNever();

        builder.Entity<Property>()
            .OwnsOne(p => p.Owner, owner =>
            {
                owner.WithOwner().HasForeignKey("Id");
                owner.Property(o => o.ClientType)
                    .HasConversion<string>()
                    .HasColumnName("owner_type")
                    .HasMaxLength(20)
                    .IsRequired();
                owner.Property(o => o.ClientId)
                    .HasColumnName("owner_id")
                    .HasMaxLength(100)
                    .IsRequired();
                owner.HasIndex(o => o.ClientId).HasDatabaseName("ix_properties_owner_client_id");
            });

        builder.Entity<Property>().OwnsOne(p => p.Address, addr =>
        {
            addr.WithOwner().HasForeignKey("Id");

            addr.Property(s => s.Street).HasColumnName("Street");
            addr.Property(s => s.Number).HasColumnName("Number");
            addr.Property(s => s.District).HasColumnName("District");
            addr.Property(s => s.City).HasColumnName("City");
            addr.Property(s => s.PostalCode).HasColumnName("PostalCode");
            addr.Property(s => s.Country).HasColumnName("Country");
        });

        builder.Entity<Property>().OwnsOne(p => p.Geolocation, geo =>
        {
            geo.WithOwner().HasForeignKey("Id");

            geo.Property(g => g.Latitude).HasColumnName("Latitude");
            geo.Property(g => g.Longitude).HasColumnName("Longitude");
            geo.Property(g => g.Accuracy).HasColumnName("Accuracy");
            geo.Property(g => g.Source).HasColumnName("Source");
        });

        builder.Entity<Property>()
            .Property(p => p.Status)
            .HasConversion<string>()
            .IsRequired();

        builder.Entity<Property>()
            .Property(p => p.IsActive)
            .IsRequired();

        builder.Entity<Property>()
            .Property(p => p.PropertyType)
            .HasConversion<string>()
            .HasColumnName("property_type")
            .HasMaxLength(20)
            .IsRequired();

        builder.Entity<Property>()
            .Property(p => p.MainPhotoProviderId)
            .HasColumnName("MainPhotoProviderId")
            .HasMaxLength(255);

        builder.Entity<Property>()
            .OwnsMany(p => p.Photos, photo =>
            {
                photo.WithOwner().HasForeignKey("PropertyId");
                photo.ToTable("property_photos");
                photo.Property(p => p.PublicUrl).HasColumnName("public_url").IsRequired();
                photo.Property(p => p.ProviderId).HasColumnName("provider_id").IsRequired();
                photo.Property(p => p.UploadedAt).HasColumnName("uploaded_at").IsRequired();
                photo.HasKey("PropertyId", "ProviderId");
            });

        builder.Entity<Property>()
            .Property(p => p.InstalledDeviceIds)
            .HasConversion(
                list => JsonSerializer.Serialize(list, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null)!)
            .HasColumnName("installed_device_ids")
            .HasColumnType("text");

        builder.Entity<Property>()
            .Property(p => p.InstalledDeviceIds)
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyCollection<string>>(
                (c1, c2) => c1!.SequenceEqual(c2!),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v!.GetHashCode())),
                c => c.ToList()));

        builder.Entity<Property>()
            .Property(p => p.HasActiveIoTMonitoring)
            .HasColumnName("has_active_iot_monitoring")
            .IsRequired();
    }

    private static void ConfigureIoTDevice(ModelBuilder builder)
    {
        builder.Entity<IoTDevice>().HasKey(d => d.Id);
        builder.Entity<IoTDevice>()
            .Property(d => d.Id)
            .HasConversion(id => id.Value, v => IoTDeviceId.From(v))
            .HasColumnName("id")
            .HasMaxLength(60)
            .IsRequired()
            .ValueGeneratedNever();

        builder.Entity<IoTDevice>()
            .Property(d => d.SerialNumber)
            .HasConversion(s => s.Value, v => SerialNumber.From(v))
            .HasColumnName("serial_number")
            .HasMaxLength(100)
            .IsRequired();
        builder.Entity<IoTDevice>().HasIndex(d => d.SerialNumber).IsUnique();

        builder.Entity<IoTDevice>()
            .Property(d => d.ApiKeyHash)
            .HasConversion(h => h.Value, v => ApiKeyHash.From(v))
            .HasColumnName("api_key_hash")
            .HasMaxLength(255)
            .IsRequired();

        builder.Entity<IoTDevice>()
            .Property(d => d.FirmwareVersion)
            .HasColumnName("firmware_version")
            .HasMaxLength(50)
            .IsRequired();

        builder.Entity<IoTDevice>()
            .Property(d => d.Status)
            .IsConcurrencyToken()
            .HasConversion<string>()
            .HasColumnName("status")
            .HasMaxLength(30)
            .IsRequired();

        builder.Entity<IoTDevice>()
            .Property(d => d.StockTechnicianId)
            .HasConversion(id => id == null ? null : id.Value, value => value == null ? null : TechnicianId.From(value))
            .HasColumnName("stock_technician_id")
            .HasMaxLength(60);
        builder.Entity<IoTDevice>().HasIndex(d => d.StockTechnicianId);

        builder.Entity<IoTDevice>()
            .Property(d => d.AssignedPropertyId)
            .HasConversion(
                id => id == null ? null : id.Value,
                v => v == null ? null : PropertyId.From(v))
            .HasColumnName("assigned_property_id")
            .HasMaxLength(60);

        builder.Entity<IoTDevice>()
            .Property(d => d.InstallationRequestId)
            .HasConversion(
                id => id == null ? null : id.Value,
                v => v == null ? null : InstallationRequestId.From(v))
            .HasColumnName("installation_request_id")
            .HasMaxLength(60);

        builder.Entity<IoTDevice>()
            .Property(d => d.InstalledByTechnicianId)
            .HasConversion(
                id => id == null ? null : id.Value,
                v => v == null ? null : TechnicianId.From(v))
            .HasColumnName("installed_by_technician_id")
            .HasMaxLength(60);

        builder.Entity<IoTDevice>()
            .Property(d => d.InstalledByStaffMemberId)
            .HasConversion(
                id => id == null ? null : id.Value,
                v => v == null ? null : StaffMemberId.From(v))
            .HasColumnName("installed_by_staff_member_id")
            .HasMaxLength(60);

        builder.Entity<IoTDevice>()
            .Property(d => d.InstalledAt)
            .HasColumnName("installed_at");

        builder.Entity<IoTDevice>()
            .Property(d => d.ConnectionStatus)
            .HasConversion<string>()
            .HasColumnName("connection_status")
            .HasMaxLength(20)
            .IsRequired();

        builder.Entity<IoTDevice>()
            .Property(d => d.LastReadingAt)
            .HasColumnName("last_reading_at");

        builder.Entity<IoTDevice>()
            .Property(d => d.MaintenanceReason)
            .HasColumnName("maintenance_reason")
            .HasMaxLength(500);

        builder.Entity<IoTDevice>()
            .Property(d => d.ExpectedReturnDate)
            .HasColumnName("expected_return_date");

        builder.Entity<IoTDevice>()
            .HasIndex(d => d.Status)
            .HasDatabaseName("idx_arm_iot_devices_status");

        builder.Entity<IoTDevice>()
            .HasIndex(d => d.AssignedPropertyId)
            .HasDatabaseName("idx_arm_iot_devices_property");
    }
}
