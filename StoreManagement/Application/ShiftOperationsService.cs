using System.Globalization;
using System.Text.Json;
using Hampcoders.Electrolink.API.Shared.Application.Time;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;

namespace Hampcoders.Electrolink.API.StoreManagement.Application;

public sealed record ScheduleStaffShiftCommand(UserId Actor, string StoreId, string WorkerCode, string WorkerName, string Role,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt);
public sealed record ListStaffShiftsQuery(UserId Actor, string StoreId, string? From, string? To);
public sealed record RemoveStaffShiftCommand(UserId Actor, string StoreId, string ShiftId);
public sealed record GetOnDutyStaffQuery(UserId Actor, string StoreId);
public sealed record RecordShiftHandoverCommand(UserId Actor, string StoreId, string WorkerId, string? WorkerName, DateOnly? ShiftDate,
    string? Notes);
public sealed record ListShiftHandoversQuery(UserId Actor, string StoreId, int Page, int PageSize);

public sealed record StaffShiftResource(string Id, string StoreId, string WorkerCode, string WorkerName, string Role,
    DateTime StartsAtUtc, DateTime EndsAtUtc, DateTime CreatedAtUtc);

/// <param name="Accountable">Worker attributed to incidents happening now (floor staff first).</param>
public sealed record OnDutyStaffResource(string StoreId, DateTime AtUtc, StaffShiftResource? Accountable, IReadOnlyList<StaffShiftResource> OnDuty);

/// <summary>State of one equipment when the shift was handed over.</summary>
public sealed record HandoverEquipmentSnapshot(string EquipmentId, string Name, string AreaId, string AreaName, string RiskLevel,
    string OperationalState, ElectricalReadingResource? LastReading);

public sealed record ShiftHandoverResource(string Id, string StoreId, string WorkerId, string WorkerName, DateOnly ShiftDate,
    DateTime RecordedAtUtc, string GridStatus, int ActiveCriticalAlerts, int ActiveNonCriticalAlerts, string? Notes,
    bool ManagerNotified, IReadOnlyList<HandoverEquipmentSnapshot> Equipment);

/// <summary>Shift roster of a store (who is on duty) and the shift handover log.</summary>
public sealed class ShiftOperationsService(StoreHierarchyService hierarchy, IKitchenOperationsRepository repository,
    StoreOperationsService operations, StoreManagerNotifier notifier)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<StaffShiftResource> Handle(ScheduleStaffShiftCommand command, CancellationToken ct)
    {
        await hierarchy.GetStore(command.Actor, command.StoreId, ct);
        var shift = StaffShiftAssignment.Schedule(command.StoreId, command.WorkerCode, command.WorkerName, command.Role,
            command.StartsAt.UtcDateTime, command.EndsAt.UtcDateTime, command.Actor.Value, DateTime.UtcNow);
        var overlapping = await repository.ShiftsOverlapping(command.StoreId, shift.StartsAtUtc, shift.EndsAtUtc, ct);
        if (overlapping.Any(s => string.Equals(s.WorkerCode, shift.WorkerCode, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("The worker already has a shift overlapping this time window.");
        repository.Add(shift);
        await repository.Save(ct);
        return Resource(shift);
    }

    /// <summary>Shifts overlapping [from, to); defaults to the 12 hours before and 24 hours after now.</summary>
    public async Task<IReadOnlyList<StaffShiftResource>> Handle(ListStaffShiftsQuery query, CancellationToken ct)
    {
        await hierarchy.GetStore(query.Actor, query.StoreId, ct);
        var now = DateTime.UtcNow;
        var interval = query.From == null && query.To == null
            ? new ExplicitUtcInterval(now.AddHours(-12), now.AddHours(24))
            : ExplicitUtcInterval.Parse(query.From, query.To);
        return (await repository.ShiftsOverlapping(query.StoreId, interval.FromUtc, interval.ToUtc, ct)).Select(Resource).ToList();
    }

    public async Task Handle(RemoveStaffShiftCommand command, CancellationToken ct)
    {
        await hierarchy.GetStore(command.Actor, command.StoreId, ct);
        var shift = await repository.FindShift(command.StoreId, command.ShiftId, ct) ?? throw new HierarchyNotFoundException();
        repository.Remove(shift);
        await repository.Save(ct);
    }

    public async Task<OnDutyStaffResource> Handle(GetOnDutyStaffQuery query, CancellationToken ct)
    {
        await hierarchy.GetStore(query.Actor, query.StoreId, ct);
        var now = DateTime.UtcNow;
        var shifts = await repository.ShiftsOverlapping(query.StoreId, now, now.AddTicks(1), ct);
        var accountable = StaffShiftAssignment.Accountable(shifts, now);
        return new(query.StoreId, now, accountable == null ? null : Resource(accountable), shifts.Select(Resource).ToList());
    }

    /// <summary>Worker accountable at an instant (no authorization: internal callers only).</summary>
    public async Task<StaffShiftAssignment?> AccountableAt(string storeId, DateTime atUtc, CancellationToken ct) =>
        StaffShiftAssignment.Accountable(await repository.ShiftsOverlapping(storeId, atUtc, atUtc.AddTicks(1), ct), atUtc);

    /// <summary>
    ///     Closes a shift: snapshots the risk and last reading of every equipment, stores the handover with the worker's
    ///     notes and notifies the store manager (email routes and in-app feed).
    /// </summary>
    public async Task<ShiftHandoverResource> Handle(RecordShiftHandoverCommand command, CancellationToken ct)
    {
        var store = await hierarchy.GetStore(command.Actor, command.StoreId, ct);
        var now = DateTime.UtcNow;
        var workerId = (command.WorkerId ?? "").Trim();
        var workerName = command.WorkerName?.Trim();
        if (string.IsNullOrEmpty(workerName))
        {
            var rostered = (await repository.ShiftsOverlapping(command.StoreId, now.AddHours(-12), now.AddTicks(1), ct))
                .LastOrDefault(s => string.Equals(s.WorkerCode, workerId, StringComparison.OrdinalIgnoreCase));
            workerName = rostered?.WorkerName ?? workerId;
        }
        var grid = await operations.Handle(new GetStoreElectricalGridStatusQuery(command.Actor, command.StoreId), ct);
        var snapshots = grid.Equipment.Select(e => new HandoverEquipmentSnapshot(e.EquipmentId, e.Name, e.AreaId, e.AreaName, e.RiskLevel,
            e.OperationalState, e.LastReading)).ToList();
        var alerts = await operations.ActiveAlertTotals(command.Actor, command.StoreId, ct);
        var handover = ShiftHandoverLog.Record(command.StoreId, workerId, workerName, command.ShiftDate ?? DateOnly.FromDateTime(now),
            JsonSerializer.Serialize(snapshots, Json), grid.Status, alerts.Critical, alerts.NonCritical, command.Notes, command.Actor.Value, now);
        repository.Add(handover);
        await repository.Save(ct);

        var resource = Resource(handover, snapshots);
        if (await notifier.Notify(command.StoreId, Notice(store, resource), ct))
        {
            handover.MarkManagerNotified();
            await repository.Save(ct);
            resource = resource with { ManagerNotified = true };
        }
        return resource;
    }

    public async Task<HierarchyPage<ShiftHandoverResource>> Handle(ListShiftHandoversQuery query, CancellationToken ct)
    {
        await hierarchy.GetStore(query.Actor, query.StoreId, ct);
        if (query.Page < 1 || query.PageSize is < 1 or > 100 || (long)(query.Page - 1) * query.PageSize > int.MaxValue)
            throw new ArgumentException("page must be positive; pageSize must be between 1 and 100.");
        var rows = await repository.ListHandovers(query.StoreId, (query.Page - 1) * query.PageSize, query.PageSize + 1, ct);
        return new(rows.Take(query.PageSize).Select(h => Resource(h,
                JsonSerializer.Deserialize<List<HandoverEquipmentSnapshot>>(h.EquipmentSnapshotsJson, Json) ?? [])).ToList(),
            query.Page, query.PageSize, rows.Count > query.PageSize);
    }

    private static StoreManagerNotice Notice(StoreResource store, ShiftHandoverResource h)
    {
        var lines = new List<string>
        {
            $"Shift handover at {store.Name} ({store.Address})",
            $"Handed over by: {h.WorkerName} ({h.WorkerId})",
            "Recorded at: " + h.RecordedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC",
            $"Electrical grid status: {h.GridStatus}",
            $"Active alerts: {h.ActiveCriticalAlerts} critical, {h.ActiveNonCriticalAlerts} non-critical", ""
        };
        lines.AddRange(h.Equipment.Where(e => e.RiskLevel != EquipmentRiskLevel.Safe)
            .Select(e => $"- {e.AreaName} / {e.Name}: {e.RiskLevel}"));
        if (h.Notes != null) { lines.Add(""); lines.Add("Notes: " + h.Notes); }
        return new($"[Electrolink] Shift handover - {store.Name}", string.Join('\n', lines), "ShiftHandover",
            new { h.Id, h.StoreId, h.WorkerId, h.WorkerName, h.RecordedAtUtc, h.GridStatus, h.ActiveCriticalAlerts, h.ActiveNonCriticalAlerts });
    }

    private static StaffShiftResource Resource(StaffShiftAssignment s) =>
        new(s.Id, s.StoreId, s.WorkerCode, s.WorkerName, s.Role, s.StartsAtUtc, s.EndsAtUtc, s.CreatedAtUtc);

    private static ShiftHandoverResource Resource(ShiftHandoverLog h, IReadOnlyList<HandoverEquipmentSnapshot> equipment) =>
        new(h.Id, h.StoreId, h.WorkerId, h.WorkerName, h.ShiftDate, h.RecordedAtUtc, h.GridStatus, h.ActiveCriticalAlerts,
            h.ActiveNonCriticalAlerts, h.Notes, h.ManagerNotified, equipment);
}
