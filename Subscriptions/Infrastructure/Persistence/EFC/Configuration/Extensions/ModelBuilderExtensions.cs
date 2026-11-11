using Hampcoders.Electrolink.API.Subscriptions.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Hampcoders.Electrolink.API.Subscriptions.Infrastructure.Persistence.EFC.Configuration.Extensions;

/// <summary>
/// Extension methods for <see cref="ModelBuilder"/> to apply Subscription and Payments configurations.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Applies the Entity Framework Core configuration for the Subscription and Payments Bounded Context.
    /// </summary>
    /// <param name="builder">The <see cref="ModelBuilder"/> instance.</param>
    public static void ApplySubscriptionsConfiguration(this ModelBuilder builder)
    {
        builder.ApplyConfiguration(new SubscriptionConfiguration());
        builder.ApplyConfiguration(new PaymentRecordConfiguration());
        builder.ApplyConfiguration(new ProcessedWebhookEventConfiguration());
        builder.ApplyConfiguration(new ContactRequestConfiguration());
    }
}