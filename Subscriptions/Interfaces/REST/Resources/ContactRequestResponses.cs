namespace Hampcoders.Electrolink.API.Subscriptions.Interfaces.REST.Resources;

/// <summary>Public receipt check of a demo request: no personal data.</summary>
/// <param name="Status">RECEIVED (stored) or NOTIFIED (the sales team was emailed).</param>
public sealed record ContactRequestStatusResponse(string Id, string Status, DateTime SubmittedAtUtc);

/// <summary>Demo lead as seen by the back office (Admin / SuperAdmin).</summary>
public sealed record ContactRequestResponse(
    string Id,
    string Name,
    string Company,
    string Email,
    string? Phone,
    string Message,
    string Status,
    DateTime SubmittedAtUtc,
    DateTime? NotifiedAtUtc);
