using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Aggregates;

namespace Hampcoders.Electrolink.API.Subscriptions.Domain.Repository;

public interface IContactRequestRepository
{
    void Add(ContactRequest request);
    /// <summary>Requests received from the address since the given time (abuse protection).</summary>
    Task<int> CountFromEmailSince(string email, DateTime sinceUtc, CancellationToken ct);
    Task Save(CancellationToken ct);
}
