using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Repository;

namespace Hampcoders.Electrolink.API.Subscriptions.Application.Internal.QueryServices;

public sealed record GetContactRequestByIdQuery(string RequestId);

/// <param name="Status">RECEIVED or NOTIFIED; null lists every request.</param>
public sealed record ListContactRequestsQuery(string? Status, int Skip, int Take);

/// <summary>Demo requests for the receipt check of the public form and for the back-office lead list.</summary>
public sealed class ContactRequestQueryService(IContactRequestRepository repository)
{
    public Task<ContactRequest?> Handle(GetContactRequestByIdQuery query, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(query.RequestId) || query.RequestId.Length > 40
            ? Task.FromResult<ContactRequest?>(null)
            : repository.FindById(query.RequestId, ct);

    public Task<(IReadOnlyList<ContactRequest> Items, int TotalCount)> Handle(ListContactRequestsQuery query, CancellationToken ct)
    {
        var status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim().ToUpperInvariant();
        if (status is not (null or ContactRequest.Received or ContactRequest.Notified))
            throw new ArgumentException($"status must be {ContactRequest.Received} or {ContactRequest.Notified}.");
        return repository.List(status, query.Skip, query.Take, ct);
    }
}
