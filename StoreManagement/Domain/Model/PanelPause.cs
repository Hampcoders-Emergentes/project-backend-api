namespace Hampcoders.Electrolink.API.StoreManagement.Domain.Model;

public static class PanelPauseStatus
{
    public const string Active = "ACTIVE";
    public const string Expired = "EXPIRED";
    public const string Ended = "ENDED";
    public const string Overridden = "OVERRIDDEN";
}

/// <summary>
///     Temporary lockout of the touchscreen of the store's control panel so staff can sanitize the screen without
///     triggering actions. A critical alert overrides the pause immediately: the screen must be usable in an emergency.
/// </summary>
public sealed class PanelPauseSession
{
    public const int MinSeconds = 15;
    public const int MaxSeconds = 300;
    public const string EndedManually = "MANUAL";
    public const string EndedByCriticalAlert = "CRITICAL_ALERT_OVERRIDE";

    public string Id { get; private set; } = null!;
    public string StoreId { get; private set; } = null!;
    public string? PanelId { get; private set; }
    public string StartedByUserId { get; private set; } = null!;
    public DateTime StartedAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public DateTime? EndedAtUtc { get; private set; }
    public string? EndReason { get; private set; }
    public string? OverridingAlertId { get; private set; }
    private PanelPauseSession() { }

    public static PanelPauseSession Start(string storeId, string? panelId, string startedByUserId, int durationSeconds, DateTime nowUtc)
    {
        if (durationSeconds is < MinSeconds or > MaxSeconds)
            throw new ArgumentException($"durationSeconds must be between {MinSeconds} and {MaxSeconds}.");
        if (panelId != null && (panelId.Trim().Length == 0 || panelId.Length > 100)) throw new ArgumentException("panelId must contain at most 100 characters.");
        return new()
        {
            Id = "pause-" + Guid.NewGuid().ToString("N"), StoreId = HierarchyValue.Id(storeId, "store-"), PanelId = panelId?.Trim(),
            StartedByUserId = startedByUserId, StartedAtUtc = nowUtc, EndsAtUtc = nowUtc.AddSeconds(durationSeconds)
        };
    }

    public bool IsActive(DateTime nowUtc) => EndedAtUtc == null && nowUtc < EndsAtUtc;

    public string StatusAt(DateTime nowUtc) => EndReason switch
    {
        EndedByCriticalAlert => PanelPauseStatus.Overridden,
        EndedManually => PanelPauseStatus.Ended,
        _ => nowUtc < EndsAtUtc ? PanelPauseStatus.Active : PanelPauseStatus.Expired
    };

    public void End(DateTime nowUtc)
    {
        if (!IsActive(nowUtc)) return;
        EndedAtUtc = nowUtc; EndReason = EndedManually;
    }

    public void OverrideFor(string alertId, DateTime nowUtc)
    {
        if (!IsActive(nowUtc)) return;
        EndedAtUtc = nowUtc; EndReason = EndedByCriticalAlert; OverridingAlertId = alertId;
    }
}
