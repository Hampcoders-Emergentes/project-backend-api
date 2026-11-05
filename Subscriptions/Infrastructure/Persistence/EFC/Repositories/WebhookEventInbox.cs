using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace Hampcoders.Electrolink.API.Subscriptions.Infrastructure.Persistence.EFC.Repositories;

public class WebhookEventInbox(AppDbContext context) : IWebhookEventInbox
{
    public async Task<bool> ProcessOnceAsync(string eventId, string eventType, Func<Task> handler)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();

        // A concurrent delivery of the same event blocks here until the first one commits (0 rows) or rolls back (1 row).
        var inserted = await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO sp_processed_webhook_events (event_id, event_type, received_at) VALUES ({eventId}, {eventType}, {DateTime.UtcNow}) ON CONFLICT (event_id) DO NOTHING");

        if (inserted == 0)
            return false;

        await handler();

        await transaction.CommitAsync();
        return true;
    }
}
