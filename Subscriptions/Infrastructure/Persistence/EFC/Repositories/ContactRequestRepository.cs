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

    public Task Save(CancellationToken ct) => context.SaveChangesAsync(ct);
}
