using Hampcoders.Electrolink.API.IAM.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.IAM.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.IAM.Domain.Repositories;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Notifications;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Hashing.BCrypt.Services;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Persistence.EFC.Repositories;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Tokens.JWT.Configuration;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Tokens.JWT.Services;
using Hampcoders.Electrolink.API.IAM.Interfaces.ACL;
using Hampcoders.Electrolink.API.IAM.Interfaces.ACL.Services;

namespace Hampcoders.Electrolink.API.IAM.Infrastructure.Infrastructure.Interfaces.ASP.Configuration.Extensions;

public static class WebApplicationBuilderExtensions
{
    public static void AddIamContextServices(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<TokenSettings>(builder.Configuration.GetSection("TokenSettings"));
        
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        builder.Services.AddScoped<IPasswordResetNotificationPort, LoggingPasswordResetNotifier>();
        builder.Services.AddScoped<IUserCommandService, UserCommandService>();
        builder.Services.AddScoped<IUserQueryService, UserQueryService>();
        builder.Services.AddScoped<ITokenService, TokenService>();
        builder.Services.AddScoped<IHashingService, HashingService>();
        builder.Services.AddScoped<IIamContextFacade, IamContextFacade>();
        builder.Services.AddScoped<ExternalProfilesService>();
        builder.Services.AddScoped<IStaffUserCommandService, StaffUserCommandService>();
    }
}