using Hampcoders.Electrolink.API.Shared.Application.Time;
using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
namespace Hampcoders.Electrolink.API.AlertManagement.Application;

/// <summary>
///     Stages alerts in the ingestion unit of work (the caller commits them with the measurement), together with
///     the external fallback dispatches the store's routes require. After the commit, <see cref="PublishCommitted"/>
///     raises <see cref="AlertRaisedEvent"/> so in-app clients are told.
/// </summary>
public sealed class AlertMaterializer(IAlertRepository repository, IAlertDispatcher dispatcher, MediatR.IMediator mediator,
    IOnDutyWorkerPort roster, ILogger<AlertMaterializer> logger)
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
        var storeId = await repository.StoreIdOfEquipment(alert.EquipmentId, ct);
        if (storeId != null) alert.AttributeOnDutyWorker(await roster.OnDutyAt(storeId, alert.CapturedAtUtc, ct));
        repository.Add(alert);
        if (!live || storeId == null) return;
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

/// <summary>Outbound port: who was on shift in the store at a given time (shift roster of StoreManagement).</summary>
public interface IOnDutyWorkerPort
{
    Task<WorkerAttribution?> OnDutyAt(string storeId, DateTime atUtc, CancellationToken ct);
}

public sealed record WorkerResource(string WorkerCode, string WorkerName);

/// <param name="IsCriticalLeakage">True for a critical CURRENT_LEAKAGE alert: clients should sound the local alarm.</param>
/// <param name="Source">TELEMETRY (sensor reading) or MANUAL_REPORT (reported by staff).</param>
/// <param name="OnDutyWorker">Worker on shift when the incident happened, from the store's shift roster.</param>
public sealed record AlertResource(string Id, string? BindingId, string? ReadingId, string EquipmentId, string AnomalyType,
    string DetectionSeverity, string Severity, DateTime CapturedAtUtc, DateTime CreatedAtUtc, string Status,
    string? AcknowledgedByUserId, DateTime? AcknowledgedAtUtc, bool IsCriticalLeakage, string Source, string? ReportCategory,
    string? Description, WorkerResource? ReportedBy, WorkerResource? OnDutyWorker, string? AssignedTechnicianId,
    string? AssignedTechnicianName, string? AssignedByUserId, DateTime? AssignedAtUtc, string? ResolvedByUserId,
    DateTime? ResolvedAtUtc, string? ResolutionNotes);

public sealed record ActiveAlertsResource(string StoreId, DateTime GeneratedAtUtc, int OpenCritical, int OpenNonCritical,
    bool HasCriticalLeakage, IReadOnlyList<AlertResource> Items);

public sealed record AlertDispatchResultResource(string AlertId, string StoreId, string Severity, bool RealtimePublished,
    int ExternalDispatchesQueued);

public sealed record ReportEquipmentAnomalyCommand(UserId Actor, string StoreId, string AreaId, string EquipmentId, string Category,
    string Description, string? ReportedByWorkerCode, string? ReportedByWorkerName);
public sealed record AssignAlertCommand(UserId Actor, string AlertId, string TechnicianId, string? TechnicianName);
public sealed record ResolveAlertCommand(UserId Actor, string AlertId, string? Notes);

/// <summary>Alert and the store it belongs to, returned by the store-less alert commands (assign, resolve).</summary>
public sealed record AlertTransitionResult(string StoreId, AlertResource Alert);

public sealed class AlertService(StoreHierarchyService hierarchy, IAlertRepository repository, IAlertDispatcher dispatcher,
    IAlertDispatchRepository dispatches, IOnDutyWorkerPort roster, MediatR.IMediator mediator, ILogger<AlertService> logger)
{
    public const int ActiveAlertsLimit = 100;
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

    /// <summary>
    ///     Staff report of a visual or audible anomaly (noise, sparks, smell...). Creates a NON_CRITICAL alert attributed to
    ///     the reporting worker (or, when none is given, the worker on shift), then routes it like any other alert.
    /// </summary>
    public async Task<AlertResource> Handle(ReportEquipmentAnomalyCommand command, CancellationToken ct)
    {
        await hierarchy.GetEquipment(command.Actor, command.StoreId, command.AreaId, command.EquipmentId, ct);
        var now = DateTime.UtcNow;
        var onDuty = await roster.OnDutyAt(command.StoreId, now, ct);
        WorkerAttribution? reporter = null;
        if (!string.IsNullOrWhiteSpace(command.ReportedByWorkerCode))
        {
            var code = command.ReportedByWorkerCode.Trim();
            var name = command.ReportedByWorkerName?.Trim();
            if (code.Length > 60 || (name != null && (name.Length == 0 || name.Length > 120)))
                throw new ArgumentException("reportedByWorkerCode must contain at most 60 characters and reportedByWorkerName at most 120.");
            reporter = new WorkerAttribution(code, name ?? (onDuty?.WorkerCode == code ? onDuty.WorkerName : code));
        }
        var alert = Alert.ReportManually(command.EquipmentId, command.Actor, command.Category, command.Description, reporter ?? onDuty, now);
        alert.AttributeOnDutyWorker(onDuty);
        repository.Add(alert);
        await dispatcher.PlanExternal(alert, command.StoreId, ct);
        await repository.Save(ct);
        var resource = Resource(alert);
        try { await mediator.Publish(new AlertRaisedEvent(command.StoreId, resource), ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[Alerts] Could not announce alert {AlertId}", alert.Id);
        }
        return resource;
    }

    /// <summary>Assigns an unresolved alert of one of the actor's stores to a technician (status ASSIGNED).</summary>
    public async Task<AlertTransitionResult> Handle(AssignAlertCommand command, CancellationToken ct)
    {
        var (alert, storeId) = await RequireForUpdate(command.Actor, command.AlertId, ct);
        alert.Assign(command.Actor, command.TechnicianId, command.TechnicianName, DateTime.UtcNow);
        await repository.Save(ct);
        return new(storeId, Resource(alert));
    }

    /// <summary>Closes the incident (status RESOLVED) with optional resolution notes.</summary>
    public async Task<AlertTransitionResult> Handle(ResolveAlertCommand command, CancellationToken ct)
    {
        var (alert, storeId) = await RequireForUpdate(command.Actor, command.AlertId, ct);
        alert.Resolve(command.Actor, command.Notes, DateTime.UtcNow);
        await repository.Save(ct);
        return new(storeId, Resource(alert));
    }

    private async Task<(Alert Alert, string StoreId)> RequireForUpdate(UserId actor, string alertId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(alertId) || alertId.Length > 70) throw new ArgumentException("Invalid alertId.");
        return await repository.FindForUpdate(actor, alertId, ct) ?? throw new HierarchyNotFoundException();
    }

    private static HierarchyPage<AlertResource> Page(List<Alert> rows, int page, int pageSize) =>
        new(rows.Take(pageSize).Select(Resource).ToList(), page, pageSize, rows.Count > pageSize);
    // Filters are case-insensitive for clients; stored values are upper case.
    private static ExplicitUtcInterval? ValidateFilters(ref string? status, ref string? severity, int page, int pageSize, string? from, string? to)
    {
        status = status?.Trim().ToUpperInvariant();
        severity = severity?.Trim().ToUpperInvariant();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue
            || (status != null && !AlertStatus.All.Contains(status)) || (severity != null && !Severities.Contains(severity)))
            throw new ArgumentException("Invalid alert filter or pagination. status: OPEN|ACKNOWLEDGED|ASSIGNED|RESOLVED; severity: CRITICAL|NON_CRITICAL.");
        return from == null && to == null ? null : ExplicitUtcInterval.Parse(from, to);
    }
    private Task StoreAccess(StoreAlertScope scope, CancellationToken ct) => scope.EquipmentId == null
        ? hierarchy.GetStore(scope.Actor, scope.StoreId, ct)
        : hierarchy.GetEquipmentInStore(scope.Actor, scope.StoreId, scope.EquipmentId, ct);
    private Task<EquipmentResource> Access(AlertScope s, CancellationToken ct) => hierarchy.GetEquipment(s.Actor, s.StoreId, s.AreaId, s.EquipmentId, ct);
    internal static AlertResource ToResource(Alert a) => Resource(a);
    private static AlertResource Resource(Alert a) => new(a.Id, a.BindingId, a.ReadingId, a.EquipmentId, a.AnomalyType,
        a.DetectionSeverity, a.Severity, a.CapturedAtUtc, a.CreatedAtUtc, a.Status, a.AcknowledgedByUserId, a.AcknowledgedAtUtc,
        a.IsCriticalLeakage, a.Source, a.ReportCategory, a.Description,
        a.ReportedByWorkerCode == null ? null : new WorkerResource(a.ReportedByWorkerCode, a.ReportedByWorkerName ?? a.ReportedByWorkerCode),
        a.OnDutyWorkerCode == null ? null : new WorkerResource(a.OnDutyWorkerCode, a.OnDutyWorkerName ?? a.OnDutyWorkerCode),
        a.AssignedTechnicianId, a.AssignedTechnicianName, a.AssignedByUserId, a.AssignedAtUtc, a.ResolvedByUserId, a.ResolvedAtUtc,
        a.ResolutionNotes);
}
