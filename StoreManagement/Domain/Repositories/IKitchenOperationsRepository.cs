using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;

namespace Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;

/// <summary>Equipment of a store with its area name, tracked for layout changes.</summary>
public sealed record PlacedEquipmentRow(ElectricalEquipment Equipment, string AreaName);

/// <summary>
///     Persistence port for kitchen floor operations: shift roster and handovers, panel pauses, safety protocols and the
///     floor plan. Callers authorize the store first.
/// </summary>
public interface IKitchenOperationsRepository
{
    void Add(StaffShiftAssignment shift);
    void Remove(StaffShiftAssignment shift);
    Task<StaffShiftAssignment?> FindShift(string storeId, string shiftId, CancellationToken ct);
    /// <summary>Shifts of the store that overlap [fromUtc, toUtc), by start time.</summary>
    Task<List<StaffShiftAssignment>> ShiftsOverlapping(string storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);

    void Add(ShiftHandoverLog handover);
    /// <summary>Newest first.</summary>
    Task<List<ShiftHandoverLog>> ListHandovers(string storeId, int skip, int take, CancellationToken ct);

    void Add(PanelPauseSession pause);
    /// <summary>Most recent pause of the store (tracked), or null.</summary>
    Task<PanelPauseSession?> LatestPause(string storeId, CancellationToken ct);

    void Add(EquipmentProtocol protocol);
    /// <summary>Stored protocol of the equipment (tracked), or null when the default applies.</summary>
    Task<EquipmentProtocol?> FindProtocol(string equipmentId, CancellationToken ct);

    /// <summary>Store owned by the actor, tracked for floor plan changes.</summary>
    Task<Store?> TrackedStore(UserId actor, string storeId, CancellationToken ct);
    /// <summary>All equipment of the store (tracked), by area then name.</summary>
    Task<List<PlacedEquipmentRow>> StoreEquipment(string storeId, CancellationToken ct);

    Task Save(CancellationToken ct);
}
