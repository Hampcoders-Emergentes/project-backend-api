using Hampcoders.Electrolink.API.Shared.Domain.Model.Entities;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration.Extensions;


using EntityFrameworkCore.CreatedUpdatedDate.Extensions;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Monitoring.Infrastructure.Persistence.EFC.Configuration.Extensions;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Subscriptions.Infrastructure.Persistence.EFC.Configuration.Extensions;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Entities;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Entities;
using Hampcoders.Electrolink.API.Assets.Infrastructure.Persistence.EFC.Configuration.Extensions;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Persistence.EFC.Configuration.Extensions;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Profiles.Infrastructure.Persistence.EFC.Configuration.Extensions;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Entities;
using Microsoft.EntityFrameworkCore;
using Hampcoders.Electrolink.API.Planning.Infrastructure.Persistence.EFC.Configuration.Extensions;
using Hampcoders.Electrolink.API.Processing.Infrastructure.Persistence.EFC.Configuration.Extensions;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Entities;
using Hampcoders.Electrolink.API.Analytics.Infrastructure.Persistence.EFC.Configurations.Extensions;

namespace Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;

/// <summary>
///     Application database context
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Property> Properties { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }
    public DbSet<OrphanedFileDeletion> OrphanedFileDeletions { get; set; }
    public DbSet<TechnicianInventory> TechnicianInventories { get; set; }
    public DbSet<Component> Components { get; set; }
    public DbSet<ComponentType> ComponentTypes { get; set; }
    public DbSet<ComponentStock> ComponentStocks { get; set; }
    public DbSet<IoTDevice> IoTDevices { get; set; }
    
    public DbSet<Profile> Profiles { get; set; }
    
    // Planning Bounded Context (Refactored with Tactical DDD)
    //public DbSet<Service> Services { get; set; } // OLD - replaced by ServiceCatalog
    //public DbSet<Request> Requests { get; set; } // OLD - replaced by ServiceRequest
    public DbSet<ServiceCatalog> ServiceCatalogs { get; set; }
    public DbSet<ServiceRecipe> ServiceRecipes { get; set; }
    public DbSet<ServiceRequest> ServiceRequests { get; set; }
    public DbSet<ServiceAssignment> ServiceAssignments { get; set; }
    
    // Monitoring Bounded Context
    public DbSet<ServiceExecution> ServiceExecutions { get; set; }
    
    // Processing Bounded Context (IoT Monitoring)
    public DbSet<DeviceReadingStream> DeviceReadingStreams { get; set; }
    public DbSet<AnomalyRecord> AnomalyRecords { get; set; }
    public DbSet<RelayControlCommand> RelayControlCommands { get; set; }
    public DbSet<Reading> Readings { get; set; }
    
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddCreatedUpdatedInterceptor();
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasPostgresExtension("postgis");

        modelBuilder.ApplyIamConfiguration();
        modelBuilder.ApplyProfilesConfiguration();
        modelBuilder.ApplyAssetsConfiguration();
        modelBuilder.ApplyServiceDesignAndPlanningConfiguration();
        modelBuilder.ApplyMonitoringConfiguration();
        modelBuilder.ApplySubscriptionsConfiguration();
        modelBuilder.ApplyAnalyticsConfiguration();
        modelBuilder.ApplyProcessingConfiguration();
        modelBuilder.ApplyConfiguration(new OrphanedFileDeletionConfiguration());
        modelBuilder.UseSnakeCaseNamingConvention();
    }
}