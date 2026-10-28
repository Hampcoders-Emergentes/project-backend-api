using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Planning.Infrastructure.Persistence.EFC.Repositories;
using Hampcoders.Electrolink.API.Planning.Interfaces.ACL;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Hampcoders.Electrolink.API.Planning.Infrastructure.Interfaces.ASP.Configuration.Extensions;

public static class InstallationAssignmentReadServices
{
    // Deliberately no Planning jobs, matching, subscriptions or provider registration.
    public static IServiceCollection AddInstallationAssignmentReads(this IServiceCollection services)
    {
        services.TryAddScoped<IServiceRequestRepository, ServiceRequestRepository>();
        services.TryAddScoped<IServiceAssignmentRepository, ServiceAssignmentRepository>();
        services.TryAddScoped<IInstallationAssignmentReader, InstallationAssignmentReader>();
        return services;
    }
}
