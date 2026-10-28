using Hampcoders.Electrolink.API.Assets.Application.ACL;
using Hampcoders.Electrolink.API.Assets.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Assets.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Assets.Infrastructure.Persistence.EFC.Repositories;
using Hampcoders.Electrolink.API.Assets.Interfaces.ACL;
using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Infrastructure.Interfaces.ASP.Configuration.Extensions;

namespace Hampcoders.Electrolink.API.Assets.Infrastructure.Interfaces.ASP.Configuration.Extensions;

public static class WebApplicationBuilderExtensions
{
    public static void AddAssetsContextService(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IPropertyRepository, PropertyRepository>();
        builder.Services.AddScoped<ITechnicianInventoryRepository, TechnicianInventoryRepository>();
        builder.Services.AddScoped<IComponentRepository, ComponentRepository>();
        builder.Services.AddScoped<IComponentTypeRepository, ComponentTypeRepository>();
        builder.Services.AddScoped<IPropertyCommandService, PropertyCommandService>();
        builder.Services.AddScoped<ITechnicianInventoryCommandService, TechnicianInventoryCommandService>();
        builder.Services.AddScoped<IComponentCommandService, ComponentCommandService>();
        builder.Services.AddScoped<IComponentTypeCommandService, ComponentTypeCommandService>();
        builder.Services.AddTransient<IPropertyQueryService, PropertyQueryService>();
        builder.Services.AddTransient<ITechnicianInventoryQueryService, TechnicianInventoryQueryService>();
        builder.Services.AddTransient<IComponentQueryService, ComponentQueryService>();
        builder.Services.AddTransient<IComponentTypeQueryService, ComponentTypeQueryService>();
        builder.Services.AddScoped<IPropertyPortfolioCommandService, PropertyPortfolioCommandService>();
        builder.Services.AddTransient<IPropertyPortfolioQueryService, PropertyPortfolioQueryService>();
        builder.Services.AddScoped<IPropertyPortfolioRepository, PropertyPortfolioRepository>();
        builder.Services.AddScoped<IAssetsContextFacade, AssetsContextFacade>();

        builder.Services.AddScoped<IComponentTypeValidator, ComponentTypeValidator>();

        builder.Services.AddInstallationAssignmentReads();
        builder.Services.AddScoped<DeviceInstallationService>();
        builder.Services.AddScoped<IIoTDeviceRepository, IoTDeviceRepository>();
        builder.Services.AddScoped<IIoTDeviceCommandService, IoTDeviceCommandService>();
        builder.Services.AddTransient<IIoTDeviceQueryService, IoTDeviceQueryService>();
    } 
}