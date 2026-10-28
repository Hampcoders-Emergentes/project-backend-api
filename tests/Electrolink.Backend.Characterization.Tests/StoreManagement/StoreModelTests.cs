using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Xunit;

namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

public sealed class StoreModelTests
{
    [Fact]
    public void New_physical_hierarchy_has_explicit_distinct_ids_and_valid_relations()
    {
        var store = Store.Register(UserId.From("us-synthetic-m31"), "Synthetic restaurant", "Synthetic address");
        var area = StoreArea.Register(store.Id, "Kitchen");
        var equipment = ElectricalEquipment.Register(area.Id, "Oven");
        Assert.StartsWith("store-", store.Id);
        Assert.Equal("us-synthetic-m31", store.OwnerUserId.Value);
        Assert.StartsWith("area-", area.Id);
        Assert.StartsWith("equipment-", equipment.Id);
        Assert.Equal(store.Id, area.StoreId);
        Assert.Equal(area.Id, equipment.AreaId);
    }

    [Fact]
    public void Invalid_names_and_identifiers_are_rejected_without_creating_relations()
    {
        Assert.Throws<ArgumentException>(() => Store.Register(UserId.From("us-synthetic-m31"), " ", "Synthetic address"));
        Assert.Throws<ArgumentException>(() => Store.Register(UserId.From("us-synthetic-m31"), "Synthetic restaurant", " "));
        Assert.Throws<ArgumentException>(() => StoreArea.Register("prop-synthetic-legacy", "Kitchen"));
        Assert.Throws<ArgumentException>(() => ElectricalEquipment.Register("dev-synthetic-sensor", "Oven"));
        Assert.Throws<ArgumentException>(() => Store.Register(UserId.From("us-synthetic-m31"), new string('x', 121), "Synthetic address"));
    }

    [Fact]
    public void Postgresql_model_is_additive_with_required_foreign_keys_and_no_legacy_entities()
    {
        using var context = new StoreManagementDbContext(new DbContextOptionsBuilder<StoreManagementDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=synthetic_m31;Username=synthetic_m31").Options);
        var types = context.Model.GetEntityTypes().ToArray();
        Assert.Equal(7, types.Length);
        Assert.All(types, type => Assert.Equal(type.ClrType == typeof(Hampcoders.Electrolink.API.AlertManagement.Domain.Alert) ? "alert_management" : "store_management", type.GetSchema()));
        var fks = types.Where(type => !type.IsOwned()).SelectMany(type => type.GetForeignKeys()).ToArray();
        Assert.Equal(6, fks.Length);
        Assert.All(fks, fk => { Assert.True(fk.IsRequired); Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior); });
        var binding = context.Model.FindEntityType(typeof(Hampcoders.Electrolink.API.StoreManagement.Domain.Model.EquipmentDeviceBinding))!;
        foreach (var key in new[] { "EquipmentId", "DeviceId", "EdgeDeviceId" })
            Assert.Contains(binding.GetIndexes(), index => index.IsUnique && index.Properties.Select(p => p.Name).SequenceEqual(new[] { key }));
        var measurement = context.Model.FindEntityType(typeof(Hampcoders.Electrolink.API.StoreManagement.Domain.Model.EquipmentMeasurement))!;
        Assert.Equal(new[] { "BindingId", "ReadingId" }, measurement.FindPrimaryKey()!.Properties.Select(p => p.Name));
        var sql = context.Database.GenerateCreateScript();
        Assert.Contains("store_management", sql);
        Assert.Contains("FOREIGN KEY", sql);
        Assert.Contains("OwnerUserId", sql);
        Assert.False(context.Model.FindEntityType(typeof(Store))!.FindProperty(nameof(Store.OwnerUserId))!.IsNullable);
        Assert.DoesNotContain("public.", sql);
        // Offline DDL only: this never opens the deliberately unusable synthetic connection.
        File.WriteAllText("/private/tmp/electrolink-m31-create-schema.sql", sql);
    }
    [Fact]
    public void Ownership_and_parent_queries_translate_to_postgresql_without_connecting()
    {
        using var context = new StoreManagementDbContext(new DbContextOptionsBuilder<StoreManagementDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=synthetic_m31;Username=synthetic_m31").Options);
        var repository = new Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence.StoreHierarchyRepository(context);
        var actor = UserId.From("us-synthetic-m31");
        var methods = new[] { ("Stores", new object[] { actor }),
            ("Areas", new object[] { actor, "store-00000000000000000000000000000001" }),
            ("Equipment", new object[] { actor, "store-00000000000000000000000000000001", "area-00000000000000000000000000000001" }) };
        foreach (var (name, args) in methods)
        {
            var method = repository.GetType().GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var query = Assert.IsAssignableFrom<IQueryable>(method.Invoke(repository, args));
            var sql = query.ToQueryString();
            Assert.Contains("OwnerUserId", sql);
            Assert.Contains("WHERE", sql);
            Assert.Contains("\"OwnerUserId\" =", sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..]);
            if (name != "Stores") Assert.Contains("@__storeId", sql);
            if (name != "Stores") Assert.Contains("JOIN", sql);
            if (name == "Equipment") Assert.Contains("AreaId", sql);
        }
    }
}
