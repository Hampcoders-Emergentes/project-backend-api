using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Hampcoders.Electrolink.API.AlertManagement.Infrastructure;

// Shares the ingestion unit of work: dispatches staged here commit together with their alert.
public sealed class AlertDispatchRepository(StoreManagementDbContext context) : IAlertDispatchRepository
{
    public void Add(AlertDispatch dispatch) => context.AlertDispatches.Add(dispatch);

    public async Task<List<(string Channel, string Destination)>> RoutesOf(string alertId, CancellationToken ct)
    {
        var stored = await context.AlertDispatches.AsNoTracking().Where(d => d.AlertId == alertId)
            .Select(d => new { d.Channel, d.Destination }).ToListAsync(ct);
        var staged = context.AlertDispatches.Local.Where(d => d.AlertId == alertId).Select(d => new { d.Channel, d.Destination });
        return stored.Concat(staged).Select(r => (r.Channel, r.Destination)).Distinct().ToList();
    }

    public Task<List<AlertDispatch>> Due(DateTime nowUtc, int take, CancellationToken ct) =>
        context.AlertDispatches.Where(d => d.Status == AlertDispatch.Pending && d.NextAttemptAtUtc <= nowUtc)
            .OrderBy(d => d.NextAttemptAtUtc).Take(take).ToListAsync(ct);

    public Task Save(CancellationToken ct) => context.SaveChangesAsync(ct);
}
