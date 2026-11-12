using Hampcoders.Electrolink.API.Processing.Domain.Repositories;
using Hampcoders.Electrolink.API.Processing.Domain.Services;
using Hampcoders.Electrolink.API.Processing.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Processing.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Processing.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Processing.Infrastructure.Persistence.EFC.Repositories;
using Hampcoders.Electrolink.API.Processing.Infrastructure.Services;
using Hampcoders.Electrolink.API.Processing.Interfaces.ACL;
using Hampcoders.Electrolink.API.Processing.Interfaces.ACL.Services;
using Hampcoders.Electrolink.API.Processing.Infrastructure.Interfaces.ASP.Configuration;

namespace Hampcoders.Electrolink.API.Processing.Infrastructure.Interfaces.ASP.Configuration.Extensions;

public static class WebApplicationBuilderExtensions
{
    public static void AddProcessingContextServices(this WebApplicationBuilder builder)
    {
        // Repositories
        builder.Services.AddScoped<IDeviceReadingStreamRepository, DeviceReadingStreamRepository>();
        builder.Services.AddScoped<IAnomalyRecordRepository, AnomalyRecordRepository>();
        builder.Services.AddScoped<IRelayControlCommandRepository, RelayControlCommandRepository>();

        // Command Services
        builder.Services.AddScoped<IDeviceReadingStreamCommandService, DeviceReadingStreamCommandService>();
        builder.Services.AddScoped<IAnomalyCommandService, AnomalyCommandService>();
        builder.Services.AddScoped<IRelayCommandService, RelayCommandService>();
        builder.Services.AddScoped<OfflineReadingsSyncCommandService>();

        // Query Services
        builder.Services.AddScoped<IIoTMonitoringQueryService, IoTMonitoringQueryService>();
        builder.Services.AddScoped<IDeviceAccessService, DeviceAccessService>();

        // Outbound Services (cross-BC)
        builder.Services.AddScoped<IServiceOperationContextFacade, ServiceOperationContextFacade>();
        builder.Services.AddScoped<IAssetsContextFacade, ExternalAssetsService>();
        builder.Services.AddScoped<IProfilesContextFacade, ExternalProfilesService>();

        // ACL
        builder.Services.AddScoped<IProcessingContextFacade, ProcessingContextFacade>();

        // MQTT Subscriber — configuración
        builder.Services.Configure<MqttConfiguration>(
            builder.Configuration.GetSection(MqttConfiguration.SectionName));

        // MQTT Subscriber — singleton compartido entre BackgroundService y HealthCheck
        builder.Services.AddSingleton<IMqttSubscriptionService, MqttDeviceSubscriberService>();
        builder.Services.AddHostedService(sp =>
            (MqttDeviceSubscriberService)sp.GetRequiredService<IMqttSubscriptionService>());

        builder.Services.AddHostedService<Hampcoders.Electrolink.API.Processing.Infrastructure.BackgroundServices.RelayCommandTimeoutWorker>();

        // MQTT Health Check — monitoreo de conectividad IoT
        builder.Services.AddHealthChecks()
            .AddCheck<MqttHealthCheck>("mqtt", tags: new[] { "iot", "readiness" });
    }
}
