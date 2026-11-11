using Hampcoders.Electrolink.API.Shared.Application.Time;
using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.StoreManagement.Application;
namespace Hampcoders.Electrolink.API.AlertManagement.Application;

/// <summary>
///     Stages alerts in the ingestion unit of work (the caller commits them with the measurement), together with
///     the external fallback dispatches the store's routes require. After the commit, <see cref="PublishCommitted"/>
///     raises <see cref="AlertRaisedEvent"/> so in-app clients are told.
/// </summary>
public sealed class AlertMaterializer(IAlertRepository repository, IAlertDispatcher dispatcher, MediatR.IMediator mediator,
    ILogger<AlertMaterializer> logger)
{
    private readonly List<(string StoreId, Alert Alert)> _staged = [];

    /// <param name="live">
    ///     False for readings replayed after a connection loss: the alert is stored for the record, but nobody is
    ///     paged about a past event (that would sound alarms for conditions that may no longer exist).
    /// </param>
    public async Task Stage(DetectedMeasurement detection, CancellationToken ct, bool live = true)
    {
        if (await repository.BySource(detection.BindingId, detection.ReadingId, ct) != null) return;
        var alert = Alert.Open(detection);
        repository.Add(alert);
        if (!live) return;
        var storeId = await repository.StoreIdOfEquipment(alert.EquipmentId, ct);
        if (storeId == null) return;
        await dispatcher.PlanExternal(alert, storeId, ct);
        _staged.Add((storeId, alert));
    }

    /// <summary>Call after the unit of work committed. Never throws.</summary>
    public async Task PublishCommitted(CancellationToken ct)
    {
        var pending = _staged.ToArray();
        _staged.Clear();
        foreach (var (storeId, alert) in pending)
        {
            try { await mediator.Publish(new AlertRaisedEvent(storeId, AlertService.ToResource(alert)), ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "[Alerts] Could not announce alert {AlertId}", alert.Id);
            }
        }
    }
}

/// <param name="IsCriticalLeakage">True for a critical CURRENT_LEAKAGE alert: clients should sound the local alarm.</param>
public sealed record AlertResource(string Id, string BindingId, string ReadingId, string EquipmentId, string AnomalyType,
    string DetectionSeverity, string Severity, DateTime CapturedAtUtc, DateTime CreatedAtUtc, string Status,
    string? AcknowledgedByUserId, DateTime? AcknowledgedAtUtc, bool IsCriticalLeakage);

public sealed record ActiveAlertsResource(string StoreId, DateTime GeneratedAtUtc, int OpenCritical, int OpenNonCritical,
    bool HasCriticalLeakage, IReadOnlyList<AlertResource> Items);

public sealed record AlertDispatchResultResource(string AlertId, string StoreId, string Severity, bool RealtimePublished,
    int ExternalDispatchesQueued);

public sealed class AlertService(StoreHierarchyService hierarchy, IAlertRepository repository, IAlertDispatcher dispatcher,
    IAlertDispatchRepository dispatches)
{
    public const int ActiveAlertsLimit = 100;
    private static readonly string[] Statuses = ["OPEN", "ACKNOWLEDGED"];
    private static readonly string[] Severities = ["CRITICAL", "NON_CRITICAL"];

    public async Task<HierarchyPage<AlertResource>> List(AlertScope scope, string? status, string? severity, int page, int pageSize, CancellationToken ct, string? from = null, string? to = null)
    {
        await Access(scope, ct);
        var interval = ValidateFilters(ref status, ref severity, page, pageSize, from, to);
        var rows = await repository.List(scope, status, severity, (page - 1) * pageSize, pageSize + 1, ct, interval?.FromUtc, interval?.ToUtc);
        return Page(rows, page, pageSize);
    }
    public async Task<HierarchyPage<AlertResource>> ListStore(StoreAlertScope scope, string? status, string? severity, int page, int pageSize, CancellationToken ct, string? from = null, string? to = null)
    {
        await StoreAccess(scope, ct);
        var interval = ValidateFilters(ref status, ref severity, page, pageSize, from, to);
        var rows = await repository.ListStore(scope, status, severity, (page - 1) * pageSize, pageSize + 1, ct, interval?.FromUtc, interval?.ToUtc);
        return Page(rows, page, pageSize);
    }

    /// <summary>Open alerts of the store (critical first, newest first), capped at <see cref="ActiveAlertsLimit"/>.</summary>
    public async Task<ActiveAlertsResource> ListActive(StoreAlertScope scope, CancellationToken ct)
    {
        await StoreAccess(scope, ct);
        var rows = await repository.ListOpen(scope, ActiveAlertsLimit, ct);
        var counts = await repository.OpenCountsByEquipment(scope.Actor, scope.StoreId, ct);
        var relevant = scope.EquipmentId == null ? counts.Values : counts.Where(kv => kv.Key == scope.EquipmentId).Select(kv => kv.Value);
        return new(scope.StoreId, DateTime.UtcNow, relevant.Sum(c => c.Critical), relevant.Sum(c => c.NonCritical),
            rows.Any(a => a.IsCriticalLeakage), rows.Select(Resource).ToList());
    }

    /// <summary>
    ///     Trusted re-dispatch of a stored alert: pushes it to in-app clients again and queues any external route
    ///     the store requires that has not been queued yet. Idempotent for external routes.
    /// </summary>
    public async Task<AlertDispatchResultResource> Dispatch(string alertId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(alertId) || alertId.Length > 70) throw new ArgumentException("Invalid alertId.");
        var alert = await repository.FindById(alertId, ct) ?? throw new HierarchyNotFoundException();
        var storeId = await repository.StoreIdOfEquipment(alert.EquipmentId, ct) ?? throw new HierarchyNotFoundException();
        var queued = await dispatcher.PlanExternal(alert, storeId, ct);
        await dispatches.Save(ct);
        var published = await dispatcher.PublishRealtime(storeId, Resource(alert), ct);
        return new(alert.Id, storeId, alert.Severity, published, queued);
    }

    public async Task<AlertResource> Get(AlertScope scope, string id, CancellationToken ct)
    {
        await Access(scope, ct);
        return Resource(await repository.Find(scope, id, ct) ?? throw new HierarchyNotFoundException());
    }
    public async Task<AlertResource> Acknowledge(AlertScope scope, string id, CancellationToken ct)
    {
        await Get(scope, id, ct);
        await repository.Acknowledge(scope, id, DateTime.UtcNow, ct);
        return await Get(scope, id, ct);
    }

    private static HierarchyPage<AlertResource> Page(List<Alert> rows, int page, int pageSize) =>
        new(rows.Take(pageSize).Select(Resource).ToList(), page, pageSize, rows.Count > pageSize);
    // Filters are case-insensitive for clients; stored values are upper case.
    private static ExplicitUtcInterval? ValidateFilters(ref string? status, ref string? severity, int page, int pageSize, string? from, string? to)
    {
        status = status?.Trim().ToUpperInvariant();
        severity = severity?.Trim().ToUpperInvariant();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue
            || (status != null && !Statuses.Contains(status)) || (severity != null && !Severities.Contains(severity)))
            throw new ArgumentException("Invalid alert filter or pagination. status: OPEN|ACKNOWLEDGED; severity: CRITICAL|NON_CRITICAL.");
        return from == null && to == null ? null : ExplicitUtcInterval.Parse(from, to);
    }
    private Task StoreAccess(StoreAlertScope scope, CancellationToken ct) => scope.EquipmentId == null
        ? hierarchy.GetStore(scope.Actor, scope.StoreId, ct)
        : hierarchy.GetEquipmentInStore(scope.Actor, scope.StoreId, scope.EquipmentId, ct);
    private Task<EquipmentResource> Access(AlertScope s, CancellationToken ct) => hierarchy.GetEquipment(s.Actor, s.StoreId, s.AreaId, s.EquipmentId, ct);
    internal static AlertResource ToResource(Alert a) => Resource(a);
    private static AlertResource Resource(Alert a) => new(a.Id, a.BindingId, a.ReadingId, a.EquipmentId, a.AnomalyType,
        a.DetectionSeverity, a.Severity, a.CapturedAtUtc, a.CreatedAtUtc, a.Status, a.AcknowledgedByUserId, a.AcknowledgedAtUtc,
        a.IsCriticalLeakage);
}
