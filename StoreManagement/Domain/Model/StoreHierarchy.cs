using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.StoreManagement.Domain.Model;

// New physical hierarchy; no implicit links to legacy Property, Component or IoTDevice.
public sealed class Store
{
    public string Id { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Address { get; private set; } = null!;
    public UserId OwnerUserId { get; private set; } = null!;
    private Store() { }
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
    private ElectricalEquipment() { }
    public static ElectricalEquipment Register(string areaId, string name) => new()
    {
        Id = "equipment-" + Guid.NewGuid().ToString("N"),
        AreaId = HierarchyValue.Id(areaId, "area-"),
        Name = HierarchyValue.Text(name, 120, nameof(name))
    };
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
