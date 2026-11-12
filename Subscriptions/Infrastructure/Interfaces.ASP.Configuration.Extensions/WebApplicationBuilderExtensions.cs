using Hampcoders.Electrolink.API.Shared.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Infrastructure;
using Hampcoders.Electrolink.API.Subscriptions.Application.ACL;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Repository;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Services;
using Hampcoders.Electrolink.API.Subscriptions.Infrastructure.BackgroundServices;
using Hampcoders.Electrolink.API.Subscriptions.Infrastructure.PaymentGateway.StripeProvider;
using Hampcoders.Electrolink.API.Subscriptions.Infrastructure.Persistence.EFC.Repositories;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.ACL;
using Microsoft.AspNetCore.RateLimiting;

namespace Hampcoders.Electrolink.API.Subscriptions.Infrastructure.Interfaces.ASP.Configuration.Extensions;

public static class WebApplicationBuilderExtensions
{
    /// <summary>Per-IP limit of the public demo request form.</summary>
    public const string ContactFormRateLimit = "contact-form";

    public static void AddSubscriptionServices(this WebApplicationBuilder builder)
    {
        // Demo requests from prospective customers (public form).
        builder.Services.Configure<DemoRequestSettings>(builder.Configuration.GetSection("Subscriptions:DemoRequests"));
        builder.Services.AddScoped<IContactRequestRepository, ContactRequestRepository>();
        builder.Services.AddScoped<ContactRequestCommandService>();
        builder.Services.AddScoped<ContactRequestQueryService>();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(ContactFormRateLimit, context => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
        });

        builder.Services.AddScoped<SubscriptionPlanPriceResolver>();
        builder.Services.Configure<SubscriptionSettings>(builder.Configuration.GetSection("Stripe"));

        builder.Services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        builder.Services.AddScoped<IWebhookEventInbox, WebhookEventInbox>();

        builder.Services.AddScoped<ISubscriptionCommandService, SubscriptionCommandService>();
        builder.Services.AddScoped<ISubscriptionQueryService, SubscriptionQueryService>();
        builder.Services.AddScoped<ExternalIamService>();

        builder.Services.AddScoped<ISubscriptionProfileResolver, SubscriptionProfileResolver>();
        builder.Services.AddScoped<ISubscriptionContextFacade, SubscriptionContextFacade>();

        builder.Services.AddHostedService<GracePeriodExpirationJob>();
        builder.Services.AddHostedService<MonthlyCounterResetJob>();
        builder.Services.AddHostedService<EnterpriseActivationPolicyJob>();

        builder.Services.AddScoped<HomeownerBillingPolicy>();
        builder.Services.AddScoped<EnterpriseBillingPolicy>();

        builder.Services.AddScoped<ISubscriptionTierQuery, SubscriptionTierQueryService>();
    }
}