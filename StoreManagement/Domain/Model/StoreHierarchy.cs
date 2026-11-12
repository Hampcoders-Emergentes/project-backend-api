using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.StoreManagement.Domain.Model;

// New physical hierarchy; no implicit links to legacy Property, Component or IoTDevice.
public sealed class Store
{
    public string Id { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Address { get; private set; } = null!;
    public UserId OwnerUserId { get; private set; } = null!;
    /// <summary>Last time the store's edge panel replayed readings buffered during a connection loss.</summary>
    public DateTime? LastEdgeSyncAtUtc { get; private set; }
    /// <summary>Size of the kitchen floor plan in layout units; equipment footprints are placed inside it.</summary>
    public double LayoutWidth { get; private set; } = DefaultLayoutWidth;
    public double LayoutHeight { get; private set; } = DefaultLayoutHeight;
    public const double DefaultLayoutWidth = 1000, DefaultLayoutHeight = 600;
    private Store() { }
    public void ResizeFloorPlan(double width, double height)
    {
        KitchenLayoutPolicy.ValidateCanvas(width, height);
        LayoutWidth = width; LayoutHeight = height;
    }
    public static Store Register(UserId ownerUserId, string name, string address) => new()
    {
        Id = "store-" + Guid.NewGuid().ToString("N"),
        OwnerUserId = UserId.From(ownerUserId.Value),
        Name = HierarchyValue.Text(name, 120, nameof(name)),
        Address = HierarchyValue.Text(address, 300, nameof(address))
    };
}

public sealed class StoreArea
{
    public string Id { get; private set; } = null!;
    public string StoreId { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    private StoreArea() { }
    public static StoreArea Register(string storeId, string name) => new()
    {
        Id = "area-" + Guid.NewGuid().ToString("N"),
        StoreId = HierarchyValue.Id(storeId, "store-"),
        Name = HierarchyValue.Text(name, 120, nameof(name))
    };
}

public sealed class ElectricalEquipment
{
    public string Id { get; private set; } = null!;
    public string AreaId { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    /// <summary>Operating limits used by anomaly evaluation and disconnection detection for this equipment.</summary>
    public ThresholdConfig Thresholds { get; private set; } = ThresholdConfig.Default();
    /// <summary>Footprint on the kitchen floor plan; all null while the equipment has not been placed.</summary>
    public double? CoordinateX { get; private set; }
    public double? CoordinateY { get; private set; }
    public double? Width { get; private set; }
    public double? Height { get; private set; }
    public int? ZIndex { get; private set; }
    private ElectricalEquipment() { }
    public EquipmentPlacement? Placement => CoordinateX is { } x && CoordinateY is { } y && Width is { } w && Height is { } h && ZIndex is { } z
        ? new EquipmentPlacement(x, y, w, h, z) : null;
    public void PlaceOnFloor(EquipmentPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        (CoordinateX, CoordinateY, Width, Height, ZIndex) = (placement.CoordinateX, placement.CoordinateY, placement.Width, placement.Height, placement.ZIndex);
    }
    public void RemoveFromFloor() => (CoordinateX, CoordinateY, Width, Height, ZIndex) = (null, null, null, null, null);
    public static ElectricalEquipment Register(string areaId, string name) => new()
    {
        Id = "equipment-" + Guid.NewGuid().ToString("N"),
        AreaId = HierarchyValue.Id(areaId, "area-"),
        Name = HierarchyValue.Text(name, 120, nameof(name)),
        Thresholds = ThresholdConfig.Default()
    };
    public void UpdateThresholds(ThresholdConfig thresholds) =>
        Thresholds = thresholds ?? throw new ArgumentNullException(nameof(thresholds));
}

internal static class HierarchyValue
{
    internal static string Text(string value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            throw new ArgumentException($"{field} is required and must contain at most {maxLength} characters.");
        return value.Trim();
    }
    internal static string Id(string value, string prefix)
    {
        if (value == null || !value.StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(value[prefix.Length..], "N", out _))
            throw new ArgumentException("Invalid hierarchy identifier.");
        return value;
    }
}
