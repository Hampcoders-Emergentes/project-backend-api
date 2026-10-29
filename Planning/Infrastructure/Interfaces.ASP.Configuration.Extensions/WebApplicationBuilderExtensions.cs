using Hampcoders.Electrolink.API.Planning.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Planning.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Infrastructure.BackgroundServices;
using Hampcoders.Electrolink.API.Planning.Infrastructure.Persistence.EFC.Repositories;

namespace Hampcoders.Electrolink.API.Planning.Infrastructure.Interfaces.ASP.Configuration.Extensions;

public static class WebApplicationBuilderExtensions
{
    public static void AddPlanningContextService(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IServiceAssignmentRepository, ServiceAssignmentRepository>();
        builder.Services.AddScoped<IServiceCatalogRepository, ServiceCatalogRepository>();
        builder.Services.AddScoped<IServiceRequestRepository, ServiceRequestRepository>();
        builder.Services.AddScoped<IServiceSuggestionRepository, ServiceSuggestionRepository>();
        builder.Services.AddHostedService<ServiceSuggestionExpirationJob>();

        builder.Services.AddScoped<IServiceAssignmentCommandService, ServiceAssignmentCommandService>();
        builder.Services.AddScoped<IServiceRequestCommandService, ServiceRequestCommandService>();
        builder.Services.AddScoped<IServiceCatalogCommandService, ServiceCatalogCommandService>();
        builder.Services.AddScoped<SubscriptionInstallationPropertyService>();

        builder.Services.AddScoped<IServiceDesignQueryService, ServiceDesignQueryService>();
        builder.Services.AddScoped<IServiceSuggestionCommandService, ServiceSuggestionCommandService>();
        builder.Services.AddScoped<IServiceSuggestionQueryService, ServiceSuggestionQueryService>();

        builder.Services.AddScoped<ExternalAssetsService>();
        builder.Services.AddScoped<ExternalProfilesService>();
        builder.Services.AddScoped<ExternalSubscriptionsService>();

        builder.Services.AddScoped<
            Hampcoders.Electrolink.API.Subscriptions.Interfaces.ACL.ISubscriptionContextFacade, 
            Hampcoders.Electrolink.API.Subscriptions.Application.ACL.SubscriptionContextFacade>();

        builder.Services.AddScoped<ExternalMonitoringService>();
        builder.Services.AddScoped<ExternalSubscriptionsService>();

        builder.Services.AddScoped<IContextSnapshotService, ContextSnapshotService>();
        builder.Services.AddScoped<
            Hampcoders.Electrolink.API.Processing.Interfaces.ACL.IProcessingContextFacade,
            Hampcoders.Electrolink.API.Processing.Interfaces.ACL.Services.ProcessingContextFacade>();

    }
}