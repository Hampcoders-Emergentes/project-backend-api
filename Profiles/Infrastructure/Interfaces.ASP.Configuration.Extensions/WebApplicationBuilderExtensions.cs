using Hampcoders.Electrolink.API.Profiles.Application.ACL;
using Hampcoders.Electrolink.API.Profiles.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Profiles.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Profiles.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Profiles.Domain.Repositories;
using Hampcoders.Electrolink.API.Profiles.Domain.Services;
using Hampcoders.Electrolink.API.Profiles.Infrastructure.ExternalProviders;
using Hampcoders.Electrolink.API.Profiles.Infrastructure.Interfaces.ASP.Configuration.Extensions.Filters;
using Hampcoders.Electrolink.API.Profiles.Infrastructure.Persistence.EFC.Repositories;
using Hampcoders.Electrolink.API.Profiles.Infrastructure.Persistence.EFC.Services;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;

namespace Hampcoders.Electrolink.API.Profiles.Infrastructure.Interfaces.ASP.Configuration.Extensions;

public static class WebApplicationBuilderExtensions
{
    public static void AddProfilesContextServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IProfileRepository, ProfileRepository>();
        builder.Services.AddScoped<IProfileCommandService, ProfileCommandService>();
        builder.Services.AddScoped<IProfileQueryService, ProfileQueryService>();
        builder.Services.AddScoped<IProfilesContextFacade, ProfilesContextFacade>();
        builder.Services.AddScoped<IProfileUniquenessChecker, ProfileUniquenessChecker>();
        builder.Services.AddScoped<ExternalIamService>();
        builder.Services.AddScoped<DomainExceptionFilter>();

        // Staff Member
        builder.Services.AddScoped<IStaffMemberRepository, StaffMemberRepository>();
        builder.Services.AddScoped<IStaffMemberCommandService, StaffMemberCommandService>();
        builder.Services.AddScoped<IStaffMemberQueryService, StaffMemberQueryService>();

        // Device Onboarding
        builder.Services.Configure<ProfilesAISettings>(
            builder.Configuration.GetSection(ProfilesAISettings.SectionName));
        builder.Services.AddHttpClient();
        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<IDeviceOnboardingService, DeviceOnboardingService>();
    }
}