using Hampcoders.Electrolink.API.AlertManagement.Application;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;
using MediatR;

namespace Hampcoders.Electrolink.API.StoreManagement.Application;

public sealed record StartPanelPauseCommand(UserId Actor, string StoreId, int DurationSeconds, string? PanelId);
public sealed record GetPanelPauseQuery(UserId Actor, string StoreId);
public sealed record EndPanelPauseCommand(UserId Actor, string StoreId);

/// <param name="Status">INACTIVE (never paused), ACTIVE, EXPIRED, ENDED or OVERRIDDEN (ended by a critical alert).</param>
/// <param name="IsInputLocked">True while the touchscreen must ignore input.</param>
public sealed record PanelPauseResource(string StoreId, string Status, bool IsInputLocked, int RemainingSeconds, string? PauseId,
    string? PanelId, DateTime? StartedAtUtc, DateTime? EndsAtUtc, DateTime? EndedAtUtc, string? EndReason, string? OverridingAlertId);

/// <summary>A pause cannot start while the store has an active critical alert.</summary>
public sealed class PanelPauseRejectedException(string message) : Exception(message);

/// <summary>
///     Touchscreen sanitation pause of a store's control panel. Panels follow the "PanelPauseChanged" realtime event;
///     a critical alert ends the pause at once so the screen is usable in an emergency.
/// </summary>
public sealed class PanelPauseService(StoreHierarchyService hierarchy, IKitchenOperationsRepository repository,
    StoreOperationsService operations, IStoreRealtimePort realtime, ILogger<PanelPauseService> logger)
{
    public const string RealtimeEvent = "PanelPauseChanged";

    public async Task<PanelPauseResource> Handle(StartPanelPauseCommand command, CancellationToken ct)
    {
        await hierarchy.GetStore(command.Actor, command.StoreId, ct);
        var alerts = await operations.ActiveAlertTotals(command.Actor, command.StoreId, ct);
        if (alerts.Critical > 0)
            throw new PanelPauseRejectedException("The panel cannot be paused while the store has active critical alerts.");
        var now = DateTime.UtcNow;
        var previous = await repository.LatestPause(command.StoreId, ct);
        previous?.End(now);
        var pause = PanelPauseSession.Start(command.StoreId, command.PanelId, command.Actor.Value, command.DurationSeconds, now);
        repository.Add(pause);
        await repository.Save(ct);
        return await Announce(Resource(command.StoreId, pause, now), ct);
    }

    public async Task<PanelPauseResource> Handle(GetPanelPauseQuery query, CancellationToken ct)
    {
        await hierarchy.GetStore(query.Actor, query.StoreId, ct);
        return Resource(query.StoreId, await repository.LatestPause(query.StoreId, ct), DateTime.UtcNow);
    }

    public async Task<PanelPauseResource> Handle(EndPanelPauseCommand command, CancellationToken ct)
    {
        await hierarchy.GetStore(command.Actor, command.StoreId, ct);
        var now = DateTime.UtcNow;
        var pause = await repository.LatestPause(command.StoreId, ct);
        if (pause == null || !pause.IsActive(now)) return Resource(command.StoreId, pause, now);
        pause.End(now);
        await repository.Save(ct);
        return await Announce(Resource(command.StoreId, pause, now), ct);
    }

    /// <summary>Ends the store's active pause because of a critical alert (internal; no authorization).</summary>
    public async Task OverrideForCriticalAlert(string storeId, string alertId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var pause = await repository.LatestPause(storeId, ct);
        if (pause == null || !pause.IsActive(now)) return;
        pause.OverrideFor(alertId, now);
        await repository.Save(ct);
        await Announce(Resource(storeId, pause, now), ct);
    }

    private async Task<PanelPauseResource> Announce(PanelPauseResource resource, CancellationToken ct)
    {
        try { await realtime.PublishAsync(resource.StoreId, RealtimeEvent, resource, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[Store] Panel pause change could not be pushed for store {StoreId}", resource.StoreId);
        }
        return resource;
    }

    private static PanelPauseResource Resource(string storeId, PanelPauseSession? p, DateTime now)
    {
        if (p == null) return new(storeId, "INACTIVE", false, 0, null, null, null, null, null, null, null);
        var active = p.IsActive(now);
        return new(storeId, p.StatusAt(now), active, active ? (int)Math.Ceiling((p.EndsAtUtc - now).TotalSeconds) : 0, p.Id, p.PanelId,
            p.StartedAtUtc, p.EndsAtUtc, p.EndedAtUtc, p.EndReason, p.OverridingAlertId);
    }
}

/// <summary>Critical alerts unlock any paused touchscreen of the store.</summary>
public sealed class PanelPauseOverrideHandler(PanelPauseService pauses, ILogger<PanelPauseOverrideHandler> logger) : INotificationHandler<AlertRaisedEvent>
{
    public async Task Handle(AlertRaisedEvent notification, CancellationToken ct)
    {
        if (notification.Alert.Severity != "CRITICAL") return;
        try { await pauses.OverrideForCriticalAlert(notification.StoreId, notification.Alert.Id, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[Store] Could not override the panel pause of store {StoreId}", notification.StoreId);
        }
    }
}
