using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;

namespace Hampcoders.Electrolink.API.StoreManagement.Application;

public sealed record EquipmentPlacementInput(string EquipmentId, double CoordinateX, double CoordinateY, double Width, double Height, int ZIndex);

public sealed record GetStoreLayoutQuery(UserId Actor, string StoreId);
/// <param name="Placements">Equipment to place or move; equipment not listed keeps its position.</param>
/// <param name="RemoveEquipmentIds">Equipment to take off the floor plan.</param>
public sealed record UpdateStoreLayoutCommand(UserId Actor, string StoreId, double? CanvasWidth, double? CanvasHeight,
    IReadOnlyList<EquipmentPlacementInput> Placements, IReadOnlyList<string> RemoveEquipmentIds);

/// <param name="RiskLevel">Live risk (SAFE, WARNING, CRITICAL_RISK, DISCONNECTED, UNKNOWN) to colour the footprint.</param>
public sealed record EquipmentLayoutResource(string EquipmentId, string Name, string AreaId, string AreaName, bool IsPlaced,
    double? CoordinateX, double? CoordinateY, double? Width, double? Height, int? ZIndex, string RiskLevel);

public sealed record StoreLayoutResource(string StoreId, double CanvasWidth, double CanvasHeight, int PlacedCount, int UnplacedCount,
    IReadOnlyList<EquipmentLayoutResource> Equipment);

/// <summary>Kitchen floor plan: where each equipment sits, for the interactive canvas.</summary>
public sealed class KitchenLayoutService(StoreHierarchyService hierarchy, IKitchenOperationsRepository repository,
    IStoreDashboardReadRepository readRepository, IStoreAlertSummaryPort alertSummary)
{
    public const int MaxPlacementsPerRequest = 500;

    public async Task<StoreLayoutResource> Handle(GetStoreLayoutQuery query, CancellationToken ct)
    {
        await hierarchy.GetStore(query.Actor, query.StoreId, ct);
        var store = await repository.TrackedStore(query.Actor, query.StoreId, ct) ?? throw HierarchyNotFoundException.Store();
        return await Resource(query.Actor, store, await repository.StoreEquipment(query.StoreId, ct), ct);
    }

    /// <summary>Applies the changes, then validates the complete resulting plan (bounds and overlaps) before saving.</summary>
    public async Task<StoreLayoutResource> Handle(UpdateStoreLayoutCommand command, CancellationToken ct)
    {
        await hierarchy.GetStore(command.Actor, command.StoreId, ct);
        var placements = command.Placements ?? [];
        var removals = command.RemoveEquipmentIds ?? [];
        if (placements.Count + removals.Count > MaxPlacementsPerRequest)
            throw new ArgumentException($"At most {MaxPlacementsPerRequest} layout changes are accepted per request.");
        var touched = placements.Select(p => p.EquipmentId).Concat(removals).ToList();
        if (touched.Distinct(StringComparer.Ordinal).Count() != touched.Count)
            throw new ArgumentException("Each equipment may appear only once across placements and removals.");

        var store = await repository.TrackedStore(command.Actor, command.StoreId, ct) ?? throw HierarchyNotFoundException.Store();
        if (command.CanvasWidth != null || command.CanvasHeight != null)
            store.ResizeFloorPlan(command.CanvasWidth ?? store.LayoutWidth, command.CanvasHeight ?? store.LayoutHeight);
        var equipment = await repository.StoreEquipment(command.StoreId, ct);
        var byId = equipment.ToDictionary(e => e.Equipment.Id, StringComparer.Ordinal);
        var unknown = touched.Where(id => !byId.ContainsKey(id)).ToList();
        if (unknown.Count > 0) throw new ArgumentException("Unknown equipment for this store: " + string.Join(", ", unknown.Take(5)) + ".");

        foreach (var p in placements)
            byId[p.EquipmentId].Equipment.PlaceOnFloor(EquipmentPlacement.Create(p.CoordinateX, p.CoordinateY, p.Width, p.Height, p.ZIndex));
        foreach (var id in removals) byId[id].Equipment.RemoveFromFloor();

        KitchenLayoutPolicy.Validate(store.LayoutWidth, store.LayoutHeight, equipment
            .Where(e => e.Equipment.Placement != null)
            .Select(e => (e.Equipment.Id, e.Equipment.Name, e.Equipment.Placement!)).ToList());
        await repository.Save(ct);
        return await Resource(command.Actor, store, equipment, ct);
    }

    private async Task<StoreLayoutResource> Resource(UserId actor, Store store, IReadOnlyList<PlacedEquipmentRow> equipment, CancellationToken ct)
    {
        var live = (await readRepository.LiveEquipment(actor, store.Id, ct)).ToDictionary(r => r.EquipmentId, StringComparer.Ordinal);
        var alerts = await alertSummary.OpenAlertCountsByEquipment(actor, store.Id, ct);
        var now = DateTime.UtcNow;
        var items = equipment.Select(row =>
        {
            var e = row.Equipment;
            alerts.TryGetValue(e.Id, out var counts);
            var risk = live.TryGetValue(e.Id, out var liveRow) ? StoreOperationsService.Assess(liveRow, counts, now).Level : EquipmentRiskLevel.Unknown;
            var p = e.Placement;
            return new EquipmentLayoutResource(e.Id, e.Name, e.AreaId, row.AreaName, p != null, p?.CoordinateX, p?.CoordinateY,
                p?.Width, p?.Height, p?.ZIndex, risk);
        }).ToList();
        return new(store.Id, store.LayoutWidth, store.LayoutHeight, items.Count(i => i.IsPlaced), items.Count(i => !i.IsPlaced), items);
    }
}
