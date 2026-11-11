namespace Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Entities;

public class OutboxMessage
{
    public const int MaxAttempts = 8;

    public Guid Id { get; set; }
    public DateTime OccurredOnUtc { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    /// <summary>Set only when the message was published successfully.</summary>
    public DateTime? ProcessedOnUtc { get; set; }
    public string? Error { get; set; }
    public int AttemptCount { get; set; }
    /// <summary>Earliest time of the next delivery attempt (exponential backoff); null = immediately.</summary>
    public DateTime? NextAttemptAtUtc { get; set; }
    /// <summary>Set when attempts are exhausted; the message is parked for manual inspection and no longer retried.</summary>
    public DateTime? DeadLetteredOnUtc { get; set; }

    public void MarkProcessed(DateTime nowUtc)
    {
        ProcessedOnUtc = nowUtc;
        Error = null;
        NextAttemptAtUtc = null;
    }

    public void MarkFailed(string error, DateTime nowUtc)
    {
        AttemptCount++;
        Error = error.Length > 2000 ? error[..2000] : error;
        if (AttemptCount >= MaxAttempts) { DeadLetteredOnUtc = nowUtc; NextAttemptAtUtc = null; return; }
        // 5s, 10s, 20s, ... capped at one hour.
        NextAttemptAtUtc = nowUtc + TimeSpan.FromSeconds(Math.Min(5 * Math.Pow(2, AttemptCount - 1), 3600));
    }
}
