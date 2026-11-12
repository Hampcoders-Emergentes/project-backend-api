namespace Hampcoders.Electrolink.API.StoreManagement.Domain.Model;

public static class StaffRole
{
    public const string OperationalStaff = "OPERATIONAL_STAFF";
    public const string Technician = "TECHNICIAN";
    public const string StoreManager = "STORE_MANAGER";
    public static readonly IReadOnlyList<string> All = [OperationalStaff, Technician, StoreManager];
}

/// <summary>
///     One worker scheduled on the store's floor for a time window (shift roster). Workers are identified by the store's
///     own employee code; they do not need a platform account. Used to attribute incidents to whoever was on duty.
/// </summary>
public sealed class StaffShiftAssignment
{
    public static readonly TimeSpan MaxShiftLength = TimeSpan.FromHours(16);

    public string Id { get; private set; } = null!;
    public string StoreId { get; private set; } = null!;
    public string WorkerCode { get; private set; } = null!;
    public string WorkerName { get; private set; } = null!;
    public string Role { get; private set; } = null!;
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string CreatedByUserId { get; private set; } = null!;
    private StaffShiftAssignment() { }

    public static StaffShiftAssignment Schedule(string storeId, string workerCode, string workerName, string role,
        DateTime startsAtUtc, DateTime endsAtUtc, string createdByUserId, DateTime nowUtc)
    {
        role = (role ?? "").Trim().ToUpperInvariant();
        if (!StaffRole.All.Contains(role)) throw new ArgumentException("role must be one of " + string.Join(", ", StaffRole.All) + ".");
        if (startsAtUtc.Kind != DateTimeKind.Utc || endsAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Shift times must be UTC.");
        if (endsAtUtc <= startsAtUtc || endsAtUtc - startsAtUtc > MaxShiftLength)
            throw new ArgumentException($"A shift must end after it starts and last at most {MaxShiftLength.TotalHours:0} hours.");
        return new()
        {
            Id = "shift-" + Guid.NewGuid().ToString("N"), StoreId = HierarchyValue.Id(storeId, "store-"),
            WorkerCode = HierarchyValue.Text(workerCode, 60, nameof(workerCode)), WorkerName = HierarchyValue.Text(workerName, 120, nameof(workerName)),
            Role = role, StartsAtUtc = startsAtUtc, EndsAtUtc = endsAtUtc, CreatedAtUtc = nowUtc, CreatedByUserId = createdByUserId
        };
    }

    public bool Covers(DateTime instantUtc) => StartsAtUtc <= instantUtc && instantUtc < EndsAtUtc;

    /// <summary>Picks the worker accountable at an instant: floor staff before managers, then the earliest shift start.</summary>
    public static StaffShiftAssignment? Accountable(IEnumerable<StaffShiftAssignment> shifts, DateTime instantUtc) =>
        shifts.Where(s => s.Covers(instantUtc))
            .OrderBy(s => s.Role == StaffRole.OperationalStaff ? 0 : s.Role == StaffRole.StoreManager ? 1 : 2)
            .ThenBy(s => s.StartsAtUtc).ThenBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault();
}

/// <summary>
///     Record of a shift closing: who handed over, the state of every piece of equipment at that moment and the notes
///     left for the next shift. The snapshot is stored as JSON so it stays readable even if the equipment changes later.
/// </summary>
public sealed class ShiftHandoverLog
{
    public string Id { get; private set; } = null!;
    public string StoreId { get; private set; } = null!;
    public string WorkerId { get; private set; } = null!;
    public string WorkerName { get; private set; } = null!;
    public DateOnly ShiftDate { get; private set; }
    public string EquipmentSnapshotsJson { get; private set; } = null!;
    public string GridStatus { get; private set; } = null!;
    public int ActiveCriticalAlerts { get; private set; }
    public int ActiveNonCriticalAlerts { get; private set; }
    public string? Notes { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public string RecordedByUserId { get; private set; } = null!;
    public bool ManagerNotified { get; private set; }
    private ShiftHandoverLog() { }

    public static ShiftHandoverLog Record(string storeId, string workerId, string workerName, DateOnly shiftDate,
        string equipmentSnapshotsJson, string gridStatus, int activeCritical, int activeNonCritical, string? notes,
        string recordedByUserId, DateTime nowUtc)
    {
        if (notes != null && notes.Trim().Length > 2000) throw new ArgumentException("notes must contain at most 2000 characters.");
        if (string.IsNullOrWhiteSpace(equipmentSnapshotsJson)) throw new ArgumentException("An equipment snapshot is required.");
        return new()
        {
            Id = "handover-" + Guid.NewGuid().ToString("N"), StoreId = HierarchyValue.Id(storeId, "store-"),
            WorkerId = HierarchyValue.Text(workerId, 60, "workerId"), WorkerName = HierarchyValue.Text(workerName, 120, nameof(workerName)),
            ShiftDate = shiftDate, EquipmentSnapshotsJson = equipmentSnapshotsJson, GridStatus = gridStatus,
            ActiveCriticalAlerts = activeCritical, ActiveNonCriticalAlerts = activeNonCritical,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(), RecordedAtUtc = nowUtc, RecordedByUserId = recordedByUserId
        };
    }

    public void MarkManagerNotified() => ManagerNotified = true;
}
