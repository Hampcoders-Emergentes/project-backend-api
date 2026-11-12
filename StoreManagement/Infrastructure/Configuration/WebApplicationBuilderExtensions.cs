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
        builder.Services.AddStoreManagement(connectionString, builder.Configuration);
    }

    public static IServiceCollection AddStoreManagement(this IServiceCollection services, string connectionString,
        IConfiguration? configuration = null)
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
        // Hub payloads use the same enum policy as the REST API (member names, not numbers).
        services.AddSignalR().AddJsonProtocol(options =>
            options.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        // Alert routing: in-app realtime (SignalR) plus a persisted queue for external fallback channels.
        services.AddScoped<IAlertNotificationPort, Hampcoders.Electrolink.API.AlertManagement.Infrastructure.Realtime.SignalRAlertNotificationAdapter>();
        services.AddScoped<IAlertRoutingPolicyPort, StoreRoutingPolicyAdapter>();
        services.AddScoped<IAlertDispatchRepository, AlertDispatchRepository>();
        services.AddScoped<IAlertDispatcher, AlertDispatcher>();
        services.AddScoped<IExternalAlertChannel, EmailAlertChannel>();
        // Third-party providers: HTTP SMS gateway when configured (otherwise SMS are logged) and signed webhooks.
        if (configuration != null)
        {
            services.Configure<AlertProviderSettings>(configuration.GetSection("Notifications"));
            services.Configure<CriticalEscalationSettings>(configuration.GetSection("Notifications:CriticalEscalation"));
        }
        if (string.IsNullOrWhiteSpace(configuration?["Notifications:Sms:GatewayUrl"]))
            services.AddScoped<IExternalAlertChannel, LoggingSmsAlertChannel>();
        else
            services.AddScoped<IExternalAlertChannel, HttpSmsGatewayAlertChannel>();
        services.AddHttpClient(nameof(HttpSmsGatewayAlertChannel));
        services.AddHttpClient(WebhookAlertChannel.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddScoped<IExternalAlertChannel, WebhookAlertChannel>();
        services.AddHostedService<AlertDispatchWorker>();
        services.AddScoped<IOnDutyWorkerPort, OnDutyWorkerAdapter>();
        services.AddScoped<Hampcoders.Electrolink.API.AlertManagement.Interfaces.ACL.IStoreAlertHistoryFacade,
            Hampcoders.Electrolink.API.AlertManagement.Interfaces.ACL.StoreAlertHistoryFacade>();

        // Kitchen floor operations: shift roster and handover, panel pause, safety protocols, floor plan.
        services.AddScoped<IKitchenOperationsRepository, KitchenOperationsRepository>();
        services.AddScoped<IStoreRealtimePort, Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Realtime.SignalRStoreRealtimeAdapter>();
        services.AddScoped<StoreManagerNotifier>();
        services.AddScoped<ShiftOperationsService>();
        services.AddScoped<PanelPauseService>();
        services.AddScoped<KitchenSafetyService>();
        services.AddScoped<KitchenLayoutService>();
        services.AddScoped<Hampcoders.Electrolink.API.StoreManagement.Interfaces.ACL.IStaffRosterFacade, Hampcoders.Electrolink.API.StoreManagement.Interfaces.ACL.StaffRosterFacade>();

        // Regulatory compliance: weekly checklists, maintenance certificates, score, signature and OHS dossier.
        services.AddScoped<IComplianceRepository, ComplianceRepository>();
        services.AddScoped<ComplianceStoreProfiles>();
        services.AddScoped<IComplianceAlertPort, AlertManagementComplianceAdapter>();
        services.AddSingleton<ISignatureImageReader, PngSignatureImageReader>();
        services.AddSingleton<IOhsReportGenerator, Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Reporting.OhsReportPdfGenerator>();
        services.AddScoped<ComplianceRecordsService>();
        services.AddScoped<ComplianceAssessmentService>();
        services.AddScoped<CertificateExpiryNotifier>();
        services.AddHostedService<Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Jobs.CertificateExpiryNotificationJob>();
        // Schema comes from this context's EF migrations (applied at startup unless Database:MigrateOnStartup=false).
        return services;
    }
}
