using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;

public sealed class StoreNotificationPreferenceRepository(StoreManagementDbContext context) : IStoreNotificationPreferenceRepository
{
    public Task<List<StoreNotificationPreference>> ListByStore(string storeId, CancellationToken ct) =>
        context.NotificationPreferences.AsNoTracking().Where(p => p.StoreId == storeId)
            .OrderBy(p => p.Channel).ThenBy(p => p.RecipientRole).ThenBy(p => p.Destination).ThenBy(p => p.Id).ToListAsync(ct);

    public async Task ReplaceForStore(string storeId, IReadOnlyList<StoreNotificationPreference> routes, CancellationToken ct)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await context.Database.BeginTransactionAsync(ct);
            await context.NotificationPreferences.Where(p => p.StoreId == storeId).ExecuteDeleteAsync(ct);
            context.NotificationPreferences.AddRange(routes);
            await context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }
}
