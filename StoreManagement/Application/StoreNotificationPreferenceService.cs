using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;

namespace Hampcoders.Electrolink.API.StoreManagement.Application;

public sealed record NotificationRouteResource(string Channel, string? Destination, string RecipientRole, string? RecipientName,
    IReadOnlyList<string> UrgencyLevels, bool Enabled);

/// <param name="IsDefault">True when the store has not configured routes and the default (in-app push only) applies.</param>
public sealed record StoreNotificationPreferencesResource(string StoreId, bool IsDefault, DateTime? UpdatedAtUtc,
    IReadOnlyList<NotificationRouteResource> Routes);

public sealed record GetStoreNotificationPreferencesQuery(UserId Actor, string StoreId);
public sealed record UpdateStoreNotificationPreferencesCommand(UserId Actor, string StoreId, IReadOnlyList<NotificationRouteResource> Routes);

/// <summary>View and replace the alert routes of a store (store manager and operational staff).</summary>
public sealed class StoreNotificationPreferenceService(StoreHierarchyService hierarchy, IStoreNotificationPreferenceRepository repository)
{
    /// <summary>Applied while a store has no routes: every alert goes to the in-app realtime feed, nothing external.</summary>
    public static readonly IReadOnlyList<NotificationRouteResource> DefaultRoutes =
    [
        new(NotificationChannel.DashboardPush, null, NotificationRecipientRole.StoreManager, null,
            [AlertUrgency.Critical, AlertUrgency.NonCritical], true)
    ];

    public async Task<StoreNotificationPreferencesResource> Handle(GetStoreNotificationPreferencesQuery query, CancellationToken ct)
    {
        await hierarchy.GetStore(query.Actor, query.StoreId, ct);
        return Resource(query.StoreId, await repository.ListByStore(query.StoreId, ct));
    }

    public async Task<StoreNotificationPreferencesResource> Handle(UpdateStoreNotificationPreferencesCommand command, CancellationToken ct)
    {
        await hierarchy.GetStore(command.Actor, command.StoreId, ct);
        if (command.Routes == null || command.Routes.Count > StoreNotificationPreference.MaxRoutesPerStore)
            throw new ArgumentException($"routes is required and may contain at most {StoreNotificationPreference.MaxRoutesPerStore} entries.");
        var now = DateTime.UtcNow;
        var routes = command.Routes.Select(r => StoreNotificationPreference.Define(command.StoreId, r.Channel, r.Destination,
            r.RecipientRole, r.RecipientName, r.UrgencyLevels ?? [], r.Enabled, now)).ToList();
        if (routes.GroupBy(r => (r.Channel, r.Destination)).Any(g => g.Count() > 1))
            throw new ArgumentException("Each channel and destination pair may appear only once.");
        await repository.ReplaceForStore(command.StoreId, routes, ct);
        return Resource(command.StoreId, routes);
    }

    /// <summary>Effective routes for one alert severity, with the default applied (no authorization: internal use).</summary>
    public async Task<IReadOnlyList<NotificationRouteResource>> EffectiveRoutes(string storeId, string severity, CancellationToken ct)
    {
        var stored = await repository.ListByStore(storeId, ct);
        var routes = stored.Count == 0 ? DefaultRoutes : stored.Select(Route).ToList();
        return routes.Where(r => r.Enabled && r.UrgencyLevels.Contains(severity)).ToList();
    }

    private static StoreNotificationPreferencesResource Resource(string storeId, IReadOnlyList<StoreNotificationPreference> stored) =>
        stored.Count == 0
            ? new(storeId, true, null, DefaultRoutes)
            : new(storeId, false, stored.Max(p => p.UpdatedAtUtc), stored.Select(Route).ToList());

    private static NotificationRouteResource Route(StoreNotificationPreference p) =>
        new(p.Channel, p.Destination, p.RecipientRole, p.RecipientName, p.Urgencies, p.Enabled);
}
