using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;

/// <summary>One row per equipment of a store: hierarchy data, binding presence and last measurement.</summary>
public sealed record EquipmentStatusRow(string EquipmentId, string Name, string AreaId, string AreaName, bool IsBound,
    int DisconnectionThresholdMin, DateTime? LastCapturedAtUtc, string? LastAnomalyType);

public interface IStoreDashboardReadRepository
{
    /// <summary>Constant number of queries regardless of equipment count; empty if the actor does not own the store.</summary>
    Task<IReadOnlyList<EquipmentStatusRow>> EquipmentStatus(UserId actor, string storeId, CancellationToken ct);
}
