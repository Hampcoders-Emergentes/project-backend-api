using Hampcoders.Electrolink.API.Shared.Application.Notifications;

namespace Hampcoders.Electrolink.API.Shared.Infrastructure.Email;

public static class EmailServiceCollectionExtensions
{
    /// <summary>Registers SMTP delivery when Email:Smtp:Host is set, otherwise the logging gateway.</summary>
    public static IServiceCollection AddEmailDelivery(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("Email");
        services.Configure<EmailSettings>(section);
        if (string.IsNullOrWhiteSpace(section["Smtp:Host"])) services.AddSingleton<IEmailSender, LoggingEmailSender>();
        else services.AddSingleton<IEmailSender, SmtpEmailSender>();
        return services;
    }
}
