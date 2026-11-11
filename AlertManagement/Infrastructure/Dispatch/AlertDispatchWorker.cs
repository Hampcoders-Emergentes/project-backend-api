using System.Globalization;
using Hampcoders.Electrolink.API.AlertManagement.Application;
using Hampcoders.Electrolink.API.AlertManagement.Domain;

namespace Hampcoders.Electrolink.API.AlertManagement.Infrastructure.Dispatch;

/// <summary>Delivers queued external alert dispatches with retries and backoff; failures end as FAILED rows.</summary>
public sealed class AlertDispatchWorker(IServiceScopeFactory scopes, ILogger<AlertDispatchWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try { await DeliverDue(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "[Alerts] Dispatch cycle failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DeliverDue(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var dispatches = scope.ServiceProvider.GetRequiredService<IAlertDispatchRepository>();
        var alerts = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
        var channels = scope.ServiceProvider.GetServices<IExternalAlertChannel>().ToDictionary(c => c.Channel);
        var due = await dispatches.Due(DateTime.UtcNow, BatchSize, ct);
        foreach (var dispatch in due)
        {
            try
            {
                if (!channels.TryGetValue(dispatch.Channel, out var channel))
                    throw new InvalidOperationException($"No adapter for channel {dispatch.Channel}.");
                var description = await alerts.Describe(dispatch.AlertId, ct) ?? throw new InvalidOperationException("Alert not found.");
                await channel.DeliverAsync(dispatch.Destination, Compose(description), ct);
                dispatch.MarkSent(DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                dispatch.MarkAttemptFailed(ex.Message, DateTime.UtcNow);
                logger.LogWarning(ex, "[Alerts] Dispatch {DispatchId} attempt {Attempt} failed", dispatch.Id, dispatch.Attempts);
            }
            await dispatches.Save(ct);
        }
    }

    private static ExternalAlertMessage Compose(AlertDescription d)
    {
        var a = d.Alert;
        var label = a.IsCriticalLeakage ? "CRITICAL CURRENT LEAKAGE" : $"{a.Severity} {a.AnomalyType}";
        var subject = $"[Electrolink] {label} - {d.EquipmentName} ({d.StoreName})";
        var lines = new List<string>
        {
            label, $"Store: {d.StoreName}", $"Area: {d.AreaName}", $"Equipment: {d.EquipmentName}",
            "Detected at: " + a.CapturedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC",
            $"Alert: {a.Id}", ""
        };
        if (a.IsCriticalLeakage) lines.Add("Disconnect the equipment and keep people away until it is inspected.");
        lines.Add("Open the Electrolink dashboard to acknowledge this alert.");
        var body = string.Join('\n', lines);
        return new(subject, body);
    }
}
