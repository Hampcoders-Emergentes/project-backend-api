namespace Hampcoders.Electrolink.API.AlertManagement.Domain;

/// <summary>
///     Queued delivery of one alert to one external fallback route (email, SMS). Written in the same
///     transaction as the alert, then delivered by a background worker with retries.
/// </summary>
public sealed class AlertDispatch
{
    public const int MaxAttempts = 5;
    public const string Pending = "PENDING";
    public const string Sent = "SENT";
    public const string Failed = "FAILED";

    public string Id { get; private set; } = null!;
    public string AlertId { get; private set; } = null!;
    public string StoreId { get; private set; } = null!;
    public string Channel { get; private set; } = null!;
    public string Destination { get; private set; } = null!;
    public string Status { get; private set; } = Pending;
    public int Attempts { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime NextAttemptAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public string? LastError { get; private set; }
    private AlertDispatch() { }

    public static AlertDispatch Queue(string alertId, string storeId, string channel, string destination, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(alertId) || string.IsNullOrWhiteSpace(storeId) || string.IsNullOrWhiteSpace(channel)
            || string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("A complete dispatch route is required.");
        return new() { Id = "dispatch-" + Guid.NewGuid().ToString("N"), AlertId = alertId, StoreId = storeId, Channel = channel,
            Destination = destination, CreatedAtUtc = nowUtc, NextAttemptAtUtc = nowUtc };
    }

    public void MarkSent(DateTime nowUtc) { Status = Sent; Attempts++; SentAtUtc = nowUtc; LastError = null; }

    /// <summary>Exponential backoff (30 s, 1 min, 2 min, ...); dead-lettered as FAILED after <see cref="MaxAttempts"/>.</summary>
    public void MarkAttemptFailed(string error, DateTime nowUtc)
    {
        Attempts++;
        LastError = error.Length > 500 ? error[..500] : error;
        if (Attempts >= MaxAttempts) { Status = Failed; return; }
        NextAttemptAtUtc = nowUtc.AddSeconds(30 * Math.Pow(2, Attempts - 1));
    }
}

public interface IAlertDispatchRepository
{
    void Add(AlertDispatch dispatch);
    /// <summary>Routes already queued for the alert, as (channel, destination) pairs.</summary>
    Task<List<(string Channel, string Destination)>> RoutesOf(string alertId, CancellationToken ct);
    Task<List<AlertDispatch>> Due(DateTime nowUtc, int take, CancellationToken ct);
    Task Save(CancellationToken ct);
}
