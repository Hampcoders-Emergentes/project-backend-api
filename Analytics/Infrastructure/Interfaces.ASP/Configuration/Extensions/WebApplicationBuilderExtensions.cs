using Hampcoders.Electrolink.API.Analytics.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Analytics.Infrastructure.ACL;
using Hampcoders.Electrolink.API.Analytics.Infrastructure.Reporting;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Analytics.Domain.Repositories;
using Hampcoders.Electrolink.API.Analytics.Domain.Services;
using Hampcoders.Electrolink.API.Analytics.Infrastructure.Jobs;
using Hampcoders.Electrolink.API.Analytics.Infrastructure.Persistence.EFC.Repositories;

namespace Hampcoders.Electrolink.API.Analytics.Infrastructure.Interfaces.ASP.Configuration.Extensions;

public static class WebApplicationBuilderExtensions
{
    public static void AddAnalyticsContextServices(this WebApplicationBuilder builder)
    {
        // Repositories
        builder.Services.AddScoped<ITechnicianMetricsRepository, TechnicianMetricsRepository>();

        // Command Services
        builder.Services.AddScoped<ITechnicianMetricsCommandService, TechnicianMetricsCommandService>();

        // Query Services
        builder.Services.AddScoped<ITechnicianMetricsQueryService, TechnicianMetricsQueryService>();

        // Store consumption analytics (ports and adapters)
        builder.Services.AddScoped<IStoreMeasurementPort, StoreManagementMeasurementAdapter>();
        builder.Services.AddSingleton<IConsumptionReportExporter, CsvConsumptionReportExporter>();
        builder.Services.AddScoped<Hampcoders.Electrolink.API.Analytics.Application.Internal.QueryServices.StoreConsumptionQueryService>();

        // Energy cost, forecasting and efficiency reports
        builder.Services.AddSingleton<IUtilityRateProvider, Hampcoders.Electrolink.API.Analytics.Infrastructure.Tariffs.ConfigurationUtilityRateProvider>();
        builder.Services.AddSingleton<IEnergyReportRenderer, EnergyReportRenderer>();
        builder.Services.AddScoped<IEnergyReportStorage, Hampcoders.Electrolink.API.Analytics.Infrastructure.Persistence.EFC.Reports.DbEnergyReportStorage>();
        builder.Services.AddScoped<EnergyCostQueryService>();
        builder.Services.AddScoped<EnergyReportCommandService>();

        // Energy planning: opening hours, budgets, period comparison, off-hours detection, predictive maintenance
        builder.Services.AddScoped<IEnergyPlanningRepository, Hampcoders.Electrolink.API.Analytics.Infrastructure.Persistence.EFC.Planning.EnergyPlanningRepository>();
        builder.Services.AddScoped<EnergyPlanningCommandService>();
        builder.Services.AddScoped<EnergyInsightsQueryService>();

        // Jobs
        builder.Services.AddScoped<TechnicianMetricsPeriodInitializerJob>();
    }
}
