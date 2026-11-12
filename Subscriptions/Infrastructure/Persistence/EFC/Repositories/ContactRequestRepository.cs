using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace Hampcoders.Electrolink.API.Subscriptions.Infrastructure.Persistence.EFC.Repositories;

public class ContactRequestRepository(AppDbContext context) : IContactRequestRepository
{
    public void Add(ContactRequest request) => context.Set<ContactRequest>().Add(request);

    public Task<int> CountFromEmailSince(string email, DateTime sinceUtc, CancellationToken ct) =>
        context.Set<ContactRequest>().CountAsync(r => r.Email == email && r.ReceivedAtUtc >= sinceUtc, ct);

    public Task<ContactRequest?> FindById(string id, CancellationToken ct) =>
        context.Set<ContactRequest>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<(IReadOnlyList<ContactRequest> Items, int TotalCount)> List(string? status, int skip, int take, CancellationToken ct)
    {
        var query = context.Set<ContactRequest>().AsNoTracking();
        if (!string.IsNullOrEmpty(status)) query = query.Where(r => r.Status == status);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(r => r.ReceivedAtUtc).ThenBy(r => r.Id).Skip(skip).Take(take).ToListAsync(ct);
        return (items, total);
    }

    public Task Save(CancellationToken ct) => context.SaveChangesAsync(ct);
}
