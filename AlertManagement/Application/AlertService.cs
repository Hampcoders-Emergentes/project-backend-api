using Hampcoders.Electrolink.API.Shared.Application.Time;
using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Application;
namespace Hampcoders.Electrolink.API.AlertManagement.Application;

// Stages only; the ingestion unit of work commits this alert with its measurement.
public sealed class AlertMaterializer(IAlertRepository repository)
{
    public async Task Stage(DetectedMeasurement detection, CancellationToken ct)
    {
        if (await repository.BySource(detection.BindingId, detection.ReadingId, ct) == null)
            repository.Add(Alert.Open(detection));
    }
}
public sealed record AlertResource(string Id, string BindingId, string ReadingId, string EquipmentId, string AnomalyType,
    string DetectionSeverity, string Severity, DateTime CapturedAtUtc, DateTime CreatedAtUtc, string Status,
    string? AcknowledgedByUserId, DateTime? AcknowledgedAtUtc);

public sealed class AlertService(StoreHierarchyService hierarchy, IAlertRepository repository)
{
    public async Task<HierarchyPage<AlertResource>> List(AlertScope scope, string? status, string? severity, int page, int pageSize, CancellationToken ct, string? from = null, string? to = null)
    {
        await Access(scope, ct);
        var interval = ValidateFilters(status, severity, page, pageSize, from, to);
        var rows = await repository.List(scope, status, severity, (page - 1) * pageSize, pageSize + 1, ct, interval?.FromUtc, interval?.ToUtc);
        return Page(rows, page, pageSize);
    }
    public async Task<HierarchyPage<AlertResource>> ListStore(StoreAlertScope scope, string? status, string? severity, int page, int pageSize, CancellationToken ct, string? from = null, string? to = null)
    {
        if (scope.EquipmentId == null) await hierarchy.GetStore(scope.Actor, scope.StoreId, ct);
        else await hierarchy.GetEquipmentInStore(scope.Actor, scope.StoreId, scope.EquipmentId, ct);
        var interval = ValidateFilters(status, severity, page, pageSize, from, to);
        var rows = await repository.ListStore(scope, status, severity, (page - 1) * pageSize, pageSize + 1, ct, interval?.FromUtc, interval?.ToUtc);
        return Page(rows, page, pageSize);
    }
    private static HierarchyPage<AlertResource> Page(List<Alert> rows, int page, int pageSize) =>
        new(rows.Take(pageSize).Select(Resource).ToList(), page, pageSize, rows.Count > pageSize);
    private static ExplicitUtcInterval? ValidateFilters(string? status, string? severity, int page, int pageSize, string? from, string? to)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue
            || (status != null && status is not ("OPEN" or "ACKNOWLEDGED")) || (severity != null && severity is not ("CRITICAL" or "NON_CRITICAL")))
            throw new ArgumentException("Invalid alert filter or pagination.");
        return from == null && to == null ? null : ExplicitUtcInterval.Parse(from, to);
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
    private Task<EquipmentResource> Access(AlertScope s, CancellationToken ct) => hierarchy.GetEquipment(s.Actor, s.StoreId, s.AreaId, s.EquipmentId, ct);
    private static AlertResource Resource(Alert a) => new(a.Id, a.BindingId, a.ReadingId, a.EquipmentId, a.AnomalyType,
        a.DetectionSeverity, a.Severity, a.CapturedAtUtc, a.CreatedAtUtc, a.Status, a.AcknowledgedByUserId, a.AcknowledgedAtUtc);
}
