using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Aggregates;

namespace Hampcoders.Electrolink.API.Subscriptions.Domain.Repository;

public interface IContactRequestRepository
{
    void Add(ContactRequest request);
    /// <summary>Requests received from the address since the given time (abuse protection).</summary>
    Task<int> CountFromEmailSince(string email, DateTime sinceUtc, CancellationToken ct);
    Task<ContactRequest?> FindById(string id, CancellationToken ct);
    /// <summary>Newest first, optionally filtered by status; returns the page and the total of matching requests.</summary>
    Task<(IReadOnlyList<ContactRequest> Items, int TotalCount)> List(string? status, int skip, int take, CancellationToken ct);
    Task Save(CancellationToken ct);
}
