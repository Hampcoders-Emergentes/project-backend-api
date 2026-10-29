using System.Text.Json;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Entities;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Hampcoders.Electrolink.API.Planning.Infrastructure.Persistence.EFC.Configuration.Extensions;

public static class ModelBuilderExtensions
{
    // ── Conversores reutilizables ──────────────────────────────────────────
    private static readonly ValueConverter<CatalogId, string> CatalogIdConverter =
        new(id => id.Value, raw => CatalogId.From(raw));

    private static readonly ValueConverter<RecipeId, string> RecipeIdConverter =
        new(id => id.Value, raw => RecipeId.From(raw));

    private static readonly ValueConverter<RequestId, string> RequestIdConverter =
        new(id => id.Value, raw => RequestId.From(raw));

    private static readonly ValueConverter<AssignmentId, string> AssignmentIdConverter =
        new(id => id.Value, raw => AssignmentId.From(raw));

    private static readonly ValueConverter<TechnicianId, string> TechnicianIdConverter =
        new(id => id.Value, raw => TechnicianId.From(raw));

    private static readonly ValueConverter<HomeownerId, string> HomeownerIdConverter =
        new(id => id.Value, raw => HomeownerId.From(raw));

    private static readonly ValueConverter<PropertyId, string> PropertyIdConverter =
        new(id => id.Value, raw => PropertyId.From(raw));
    
    public static void ApplyServiceDesignAndPlanningConfiguration(this ModelBuilder builder)
    {
        ConfigureServiceSuggestion(builder);

        // ── ServiceCatalog ────────────────────────────────────────────────
        builder.Entity<ServiceCatalog>().HasKey(c => c.CatalogId);

        builder.Entity<ServiceCatalog>()
            .Property(c => c.CatalogId)
            .HasConversion(CatalogIdConverter)
            .HasColumnName("id")
            .HasMaxLength(60)
            .IsRequired()
            .ValueGeneratedNever();

        builder.Entity<ServiceCatalog>()
            .Property(c => c.TechnicianId)
            .HasConversion(TechnicianIdConverter)
            .HasColumnName("technician_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Entity<ServiceCatalog>().HasIndex(c => c.TechnicianId).IsUnique();

        builder.Entity<ServiceCatalog>()
            .Property(c => c.Status)
            .HasConversion<string>()
            .HasColumnName("status")
            .HasMaxLength(20)
            .IsRequired();

        builder.Entity<ServiceCatalog>()
            .HasMany(c => c.Recipes)
            .WithOne()
            .HasForeignKey(r => r.CatalogId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ServiceCatalog>().Property(c => c.CreatedDate).HasColumnName("created_at").IsRequired();
        builder.Entity<ServiceCatalog>().Property(c => c.UpdatedDate).HasColumnName("updated_at");
        builder.Entity<ServiceCatalog>().Ignore(c => c.DomainEvents);

        // ── ServiceRecipe ─────────────────────────────────────────────────
        builder.Entity<ServiceRecipe>().HasKey(r => r.Id);

        builder.Entity<ServiceRecipe>()
            .Property(r => r.Id)
            .HasConversion(RecipeIdConverter)
            .HasColumnName("id")
            .HasMaxLength(60)
            .IsRequired()
            .ValueGeneratedNever();

        builder.Entity<ServiceRecipe>().Property(r => r.CatalogId).HasConversion(CatalogIdConverter).HasColumnName("catalog_id").HasMaxLength(60).IsRequired();
        builder.Entity<ServiceRecipe>().Property(r => r.TechnicianId).HasConversion(TechnicianIdConverter).HasColumnName("technician_id").HasMaxLength(100).IsRequired();
        builder.Entity<ServiceRecipe>().Property(c => c.ServiceCategory).HasConversion<string>().HasColumnName("service_category").HasMaxLength(20).IsRequired();
        builder.Entity<ServiceRecipe>().Property(r => r.ServiceName).HasColumnName("service_name").HasMaxLength(200).IsRequired();
        builder.Entity<ServiceRecipe>().Property(r => r.ServiceDescription).HasColumnName("service_description").HasColumnType("text").IsRequired();
        builder.Entity<ServiceRecipe>().Property(r => r.ServiceCategory).HasConversion<string>().HasColumnName("service_category").HasMaxLength(50).IsRequired();

        builder.Entity<ServiceRecipe>()
            .HasMany(r => r.ComponentRequirements)
            .WithOne()
            .HasForeignKey("ShadowRecipeId") 
            .HasConstraintName("fk_requirements_recipes")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ServiceRecipe>()
            .OwnsOne(r => r.EstimatedDuration, ed =>
            {
                ed.WithOwner().HasForeignKey("Id");
                ed.Property(d => d.TotalMinutes).HasColumnName("estimated_duration_min").IsRequired();
            });

        builder.Entity<ServiceRecipe>()
            .OwnsOne(r => r.Pricing, sp =>
            {
                sp.WithOwner().HasForeignKey("Id");

                sp.OwnsOne(p => p.MaterialsEstimate, m =>
                {
                    m.WithOwner().HasForeignKey("Id");
                    m.Property(v => v.Amount).HasColumnName("materials_estimate").HasColumnType("decimal(10,2)");
                    m.Property(v => v.Currency).HasConversion<string>().HasColumnName("currency").HasMaxLength(3);
                });

                sp.OwnsOne(p => p.LaborCost, l =>
                {
                    l.WithOwner().HasForeignKey("Id");
                    l.Property(v => v.Amount).HasColumnName("labor_cost").HasColumnType("decimal(10,2)");
                });

                sp.OwnsOne(p => p.TotalPrice, t =>
                {
                    t.WithOwner().HasForeignKey("Id");
                    t.Property(v => v.Amount).HasColumnName("total_price").HasColumnType("decimal(10,2)");
                });
            });

        builder.Entity<ServiceRecipe>().Property(r => r.Prerequisites).HasColumnName("prerequisites").HasColumnType("jsonb");
        builder.Entity<ServiceRecipe>().Property(r => r.Deliverables).HasColumnName("deliverables").HasColumnType("jsonb");

        builder.Entity<ServiceRecipe>()
            .OwnsOne(r => r.WarrantyPeriod, wp =>
            {
                wp.WithOwner().HasForeignKey("Id");
                wp.Property(w => w.Months).HasColumnName("warranty_months").IsRequired();
            });

        builder.Entity<ServiceRecipe>().Property(r => r.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Entity<ServiceRecipe>().Property(r => r.TimesRequested).HasColumnName("times_requested").HasDefaultValue(0).IsRequired();

        // ── ComponentRequirementItem ──────────────────────────────────────
        builder.Entity<ComponentRequirementItem>().HasKey(c => c.Id);

        builder.Entity<ComponentRequirementItem>()
            .Property(c => c.Id)
            .HasColumnName("id")
            .HasMaxLength(60)
            .IsRequired()
            .ValueGeneratedNever();

        builder.Entity<ComponentRequirementItem>().Ignore(c => c.RecipeId);

        builder.Entity<ComponentRequirementItem>()
            .Property<RecipeId>("ShadowRecipeId")
            .HasConversion(RecipeIdConverter)
            .HasColumnName("recipe_id")
            .HasMaxLength(60)
            .IsRequired();

        builder.Entity<ComponentRequirementItem>().Property(c => c.ComponentTypeId).HasColumnName("component_type_id").HasMaxLength(100).IsRequired();
        builder.Entity<ComponentRequirementItem>().Property(c => c.ComponentTypeName).HasColumnName("component_type_name").HasMaxLength(200).IsRequired();
        builder.Entity<ComponentRequirementItem>().Property(c => c.Quantity).HasColumnName("quantity").IsRequired();
        builder.Entity<ComponentRequirementItem>().Property(c => c.IsRequired).HasColumnName("is_required").HasDefaultValue(true).IsRequired();

        // ── ServiceRequest ────────────────────────────────────────────────
        builder.Entity<ServiceRequest>().HasKey(r => r.RequestId);

        builder.Entity<ServiceRequest>()
            .Property(r => r.RequestId)
            .HasConversion(RequestIdConverter)
            .HasColumnName("id")
            .HasMaxLength(60)
            .IsRequired()
            .ValueGeneratedNever();

        builder.Entity<ServiceRequest>()
            .OwnsOne(r => r.Client, client =>
            {
                client.WithOwner().HasForeignKey("RequestId");
                client.Property("RequestId").HasColumnName("id");
                client.Property(c => c.ClientType)
                    .HasConversion<string>()
                    .HasColumnName("client_type")
                    .HasMaxLength(20)
                    .IsRequired();
                client.Property(c => c.ClientId)
                    .HasColumnName("client_id")
                    .HasMaxLength(100)
                    .IsRequired();
            });

        builder.Entity<ServiceRequest>()
            .Property(r => r.Status)
            .HasConversion<string>()
            .HasColumnName("status")
            .HasMaxLength(30)
            .IsRequired()
            // Metadata only (no DDL): competing transitions from the same read status cannot both save.
            .IsConcurrencyToken();

        builder.Entity<ServiceRequest>()
            .Property(r => r.PropertyId)
            .HasConversion(
                id => id == null ? null : id.Value,
                val => val == null ? null : PropertyId.From(val))
            .HasColumnName("property_id")
            .HasMaxLength(100);

        builder.Entity<ServiceRequest>()
            .OwnsOne(r => r.Geolocation, geo =>
            {
                geo.WithOwner().HasForeignKey("Id");

                geo.Property(g => g.Latitude)
                    .HasColumnName("geolocation_lat")
                    .HasColumnType("decimal(9,6)");
                geo.Property(g => g.Longitude)
                    .HasColumnName("geolocation_lon")
                    .HasColumnType("decimal(9,6)");
                geo.Property(g => g.Accuracy)
                    .HasColumnName("geolocation_accuracy");
                geo.Property(g => g.Source)
                    .HasColumnName("geolocation_source")
                    .HasMaxLength(20);
            });

        builder.Entity<ServiceRequest>()
            .Property(r => r.SelectedRecipeId)
            .HasConversion(
                id => id == null ? null : id.Value,
                val => val == null ? null : RecipeId.From(val))
            .HasColumnName("selected_recipe_id")
            .HasMaxLength(60);

        builder.Entity<ServiceRequest>()
            .Property(r => r.SelectedTechnicianId)
            .HasConversion(
                id => id == null ? null : id.Value,
                val => val == null ? null : TechnicianId.From(val))
            .HasColumnName("selected_technician_id")
            .HasMaxLength(100);

        builder.Entity<ServiceRequest>()
            .Property(r => r.RecipeSnapshot)
            .HasColumnName("recipe_snapshot")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => v == null ? null : JsonSerializer.Deserialize<RecipeSnapshot>(
                    v, (JsonSerializerOptions?)null));

        builder.Entity<ServiceRequest>()
            .Property(r => r.Preferences)
            .HasColumnName("preferences")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => v == null ? null : JsonSerializer.Deserialize<RequestPreferences>(
                    v, (JsonSerializerOptions?)null));

        builder.Entity<ServiceRequest>().Property(r => r.IsPriority).HasColumnName("is_priority").HasDefaultValue(false).IsRequired();

        builder.Entity<ServiceRequest>()
            .Property(r => r.AssignmentId)
            .HasConversion(
                id => id == null ? null : id.Value,
                val => val == null ? null : AssignmentId.From(val))
            .HasColumnName("assignment_id")
            .HasMaxLength(60);

        builder.Entity<ServiceRequest>()
            .Property(r => r.AssignedStaffMemberId)
            .HasConversion(
                id => id == null ? null : id.Value,
                val => val == null ? null : StaffMemberId.From(val))
            .HasColumnName("assigned_staff_member_id")
            .HasMaxLength(60);

        builder.Entity<ServiceRequest>().Property(r => r.CreatedDate).HasColumnName("created_at").IsRequired();
        builder.Entity<ServiceRequest>().Property(r => r.UpdatedDate).HasColumnName("updated_at");
        builder.Entity<ServiceRequest>().Ignore(r => r.DomainEvents);
        builder.Entity<ServiceRequest>().Ignore(r => r.IotContextSnapshot);

        // ── ServiceAssignment ─────────────────────────────────────────────
        builder.Entity<ServiceAssignment>().HasKey(a => a.AssignmentId);

        builder.Entity<ServiceAssignment>()
            .Property(a => a.AssignmentId)
            .HasConversion(AssignmentIdConverter)
            .HasColumnName("id")
            .HasMaxLength(60)
            .IsRequired()
            .ValueGeneratedNever();

        builder.Entity<ServiceAssignment>()
            .Property(a => a.RequestId)
            .HasConversion(RequestIdConverter)
            .HasColumnName("request_id")
            .HasMaxLength(60)
            .IsRequired();

        builder.Entity<ServiceAssignment>()
            .Property(a => a.TechnicianId)
            .HasConversion(
                id => id == null ? null : id.Value,
                val => val == null ? null : TechnicianId.From(val))
            .HasColumnName("technician_id")
            .HasMaxLength(100);

        builder.Entity<ServiceAssignment>()
            .Property(a => a.StaffMemberId)
            .HasConversion(
                id => id == null ? null : id.Value,
                val => val == null ? null : StaffMemberId.From(val))
            .HasColumnName("staff_member_id")
            .HasMaxLength(60);

        builder.Entity<ServiceAssignment>()
            .Property(a => a.Status)
            .HasConversion<string>()
            .HasColumnName("status")
            .HasMaxLength(20)
            .IsRequired();

        builder.Entity<ServiceAssignment>()
            .Property(a => a.RecipeSnapshot)
            .HasColumnName("recipe_snapshot")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => v == null ? null : JsonSerializer.Deserialize<RecipeSnapshot>(
                    v, (JsonSerializerOptions?)null));

        builder.Entity<ServiceAssignment>()
            .Property(a => a.MatchingCriteria)
            .HasColumnName("matching_criteria")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => v == null ? null : JsonSerializer.Deserialize<MatchingCriteria>(
                    v, (JsonSerializerOptions?)null));

        builder.Entity<ServiceAssignment>()
            .Property(a => a.MatchingScore)
            .HasColumnName("matching_score")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => v == null ? null : JsonSerializer.Deserialize<MatchingScore>(
                    v, (JsonSerializerOptions?)null));

        builder.Entity<ServiceAssignment>().Property(a => a.FailureReason).HasColumnName("failure_reason").HasMaxLength(100);
        builder.Entity<ServiceAssignment>().Property(a => a.RetryCount).HasColumnName("retry_count").HasDefaultValue(0).IsRequired();
        builder.Entity<ServiceAssignment>().Property(a => a.CreatedDate).HasColumnName("created_at").IsRequired();
        builder.Entity<ServiceAssignment>().Ignore(a => a.DomainEvents);
    }

    private static void ConfigureServiceSuggestion(ModelBuilder builder)
    {
        builder.Entity<ServiceSuggestion>().ToTable("service_suggestions");
        builder.Entity<ServiceSuggestion>().HasKey(s => s.SuggestionId);
        builder.Entity<ServiceSuggestion>().Property(s => s.SuggestionId).HasColumnName("suggestion_id").HasMaxLength(50).IsRequired();
        builder.Entity<ServiceSuggestion>().Property(s => s.ClientId).HasColumnName("client_id").HasMaxLength(100).IsRequired();
        builder.Entity<ServiceSuggestion>().Property(s => s.ProfileId).HasColumnName("profile_id").HasMaxLength(50);
        builder.Entity<ServiceSuggestion>().Property(s => s.PropertyId).HasColumnName("property_id").HasMaxLength(50).IsRequired();
        builder.Entity<ServiceSuggestion>().Property(s => s.AnomalyId).HasColumnName("anomaly_id").HasMaxLength(50).IsRequired();
        builder.Entity<ServiceSuggestion>().Property(s => s.DeviceId).HasColumnName("device_id").HasMaxLength(60).IsRequired();
        builder.Entity<ServiceSuggestion>().Property(s => s.AnomalyType).HasColumnName("anomaly_type").HasMaxLength(100).IsRequired();
        builder.Entity<ServiceSuggestion>().Property(s => s.Severity).HasColumnName("severity").HasMaxLength(20).IsRequired();
        builder.Entity<ServiceSuggestion>().Property(s => s.Status).HasConversion<string>().HasColumnName("status").HasMaxLength(30).IsRequired();
        builder.Entity<ServiceSuggestion>().Property(s => s.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Entity<ServiceSuggestion>().Property(s => s.ViewedAt).HasColumnName("viewed_at");
        builder.Entity<ServiceSuggestion>().Property(s => s.ConvertedToRequestAt).HasColumnName("converted_to_request_at");
        builder.Entity<ServiceSuggestion>().HasIndex(s => s.ClientId).HasDatabaseName("ix_suggestions_client_id");
        builder.Entity<ServiceSuggestion>().HasIndex(s => new { s.Status, s.ExpiresAt }).HasDatabaseName("ix_suggestions_status_expires");
    }
}