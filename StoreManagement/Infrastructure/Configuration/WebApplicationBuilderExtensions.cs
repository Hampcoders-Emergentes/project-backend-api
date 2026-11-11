using Hampcoders.Electrolink.API.AlertManagement.Application;
using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.AlertManagement.Infrastructure;
using Hampcoders.Electrolink.API.AlertManagement.Infrastructure.Dispatch;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;
using Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Services;

namespace Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Configuration;

public static class WebApplicationBuilderExtensions
{
    public static void AddStoreManagementContextService(this WebApplicationBuilder builder, string connectionString)
    {
        builder.Services.AddStoreManagement(connectionString);
    }

    public static IServiceCollection AddStoreManagement(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<StoreManagementDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IStoreHierarchyRepository, StoreHierarchyRepository>();
        services.AddScoped<StoreHierarchyService>();
        services.AddScoped<IEquipmentTelemetryRepository, EquipmentTelemetryRepository>();
        services.AddScoped<IDeviceLinkAuthority, ExistingAssetsDeviceLinkAuthority>();
        services.AddScoped<EquipmentTelemetryService>();
        services.AddScoped<IAlertRepository, AlertRepository>();
        services.AddScoped<AlertMaterializer>();
        services.AddScoped<AlertService>();
        services.AddScoped<EquipmentThresholdService>();
        services.AddScoped<IStoreDashboardReadRepository, StoreDashboardReadRepository>();
        services.AddScoped<IStoreAlertSummaryPort, Hampcoders.Electrolink.API.AlertManagement.Infrastructure.StoreAlertSummaryAdapter>();
        services.AddScoped<StoreDashboardService>();
        services.AddScoped<Hampcoders.Electrolink.API.StoreManagement.Interfaces.ACL.IStoreMeasurementFacade, Hampcoders.Electrolink.API.StoreManagement.Interfaces.ACL.StoreMeasurementFacade>();
        services.AddScoped<StoreOperationsService>();
        services.AddScoped<IStoreNotificationPreferenceRepository, StoreNotificationPreferenceRepository>();
        services.AddScoped<StoreNotificationPreferenceService>();
        services.AddScoped<Hampcoders.Electrolink.API.StoreManagement.Interfaces.ACL.IStoreNotificationRoutesFacade, Hampcoders.Electrolink.API.StoreManagement.Interfaces.ACL.StoreNotificationRoutesFacade>();
        services.AddScoped<Hampcoders.Electrolink.API.StoreManagement.Interfaces.ACL.IStoreAccessFacade, Hampcoders.Electrolink.API.StoreManagement.Interfaces.ACL.StoreAccessFacade>();
        services.AddSignalR();
        // Alert routing: in-app realtime (SignalR) plus a persisted queue for external fallback channels.
        services.AddScoped<IAlertNotificationPort, Hampcoders.Electrolink.API.AlertManagement.Infrastructure.Realtime.SignalRAlertNotificationAdapter>();
        services.AddScoped<IAlertRoutingPolicyPort, StoreRoutingPolicyAdapter>();
        services.AddScoped<IAlertDispatchRepository, AlertDispatchRepository>();
        services.AddScoped<IAlertDispatcher, AlertDispatcher>();
        services.AddScoped<IExternalAlertChannel, EmailAlertChannel>();
        services.AddScoped<IExternalAlertChannel, LoggingSmsAlertChannel>();
        services.AddHostedService<AlertDispatchWorker>();
        // Schema comes from this context's EF migrations (applied at startup unless Database:MigrateOnStartup=false).
        return services;
    }
}
