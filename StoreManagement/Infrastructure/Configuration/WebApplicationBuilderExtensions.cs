using Hampcoders.Electrolink.API.AlertManagement.Application;
using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.AlertManagement.Infrastructure;
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
        // Provisioning is explicit and separate: no EnsureCreated or Migrate on startup.
        return services;
    }
}
