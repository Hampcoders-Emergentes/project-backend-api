using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

public sealed class StoreHttpTests
{
    [Fact]
    public async Task Human_can_create_and_query_hierarchy_with_server_assigned_ownership()
    {
        using var h = new StoreHttpScenario();
        var store = await h.Create("/api/v1/stores", new { name = "Synthetic restaurant", address = "Synthetic address" });
        var storeId = store.GetProperty("id").GetString()!;
        Assert.Equal(h.Owner.Id.Value, store.GetProperty("ownerUserId").GetString());
        var area = await h.Create($"/api/v1/stores/{storeId}/areas", new { name = "Kitchen" });
        var areaId = area.GetProperty("id").GetString()!;
        var equipmentPath = $"/api/v1/stores/{storeId}/areas/{areaId}/equipment";
        var equipment = await h.Create(equipmentPath, new { name = "Oven" });
        Assert.Equal(storeId, area.GetProperty("storeId").GetString());
        Assert.Equal(areaId, equipment.GetProperty("areaId").GetString());
        foreach (var path in new[] { $"/api/v1/stores/{storeId}", $"/api/v1/stores/{storeId}/areas/{areaId}",
            equipmentPath + "/" + equipment.GetProperty("id").GetString() })
            Assert.Equal(HttpStatusCode.OK, (await h.Send(HttpMethod.Get, path)).StatusCode);
        foreach (var path in new[] { "/api/v1/stores", $"/api/v1/stores/{storeId}/areas", equipmentPath })
        {
            var response = await h.Send(HttpMethod.Get, path);
            response.EnsureSuccessStatusCode();
            var page = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Single(page.GetProperty("items").EnumerateArray());
            Assert.Equal(20, page.GetProperty("pageSize").GetInt32());
            Assert.False(page.GetProperty("hasMore").GetBoolean());
        }
        Assert.Equal(3, h.Repository.ConfirmedSaves);
    }

    [Fact]
    public async Task Other_authenticated_admin_cannot_read_write_or_list_another_users_hierarchy()
    {
        using var h = new StoreHttpScenario();
        var store = await h.Create("/api/v1/stores", new { name = "Synthetic restaurant", address = "Synthetic address" });
        var root = "/api/v1/stores/" + store.GetProperty("id").GetString();
        var area = await h.Create(root + "/areas", new { name = "Kitchen" });
        var areaPath = root + "/areas/" + area.GetProperty("id").GetString();
        var equipment = await h.Create(areaPath + "/equipment", new { name = "Oven" });
        foreach (var path in new[] { root, root + "/areas", areaPath, areaPath + "/equipment",
            areaPath + "/equipment/" + equipment.GetProperty("id").GetString() })
            Assert.Equal(HttpStatusCode.NotFound, (await h.Send(HttpMethod.Get, path, otherUser: true)).StatusCode);
        foreach (var path in new[] { root + "/areas", areaPath + "/equipment" })
            Assert.Equal(HttpStatusCode.NotFound, (await h.Send(HttpMethod.Post, path, new { name = "Unauthorized" }, otherUser: true)).StatusCode);
        var page = await (await h.Send(HttpMethod.Get, "/api/v1/stores", otherUser: true)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.Equal(3, h.Repository.ConfirmedSaves);
    }

    [Fact]
    public async Task Known_ids_cannot_be_mixed_across_parent_paths()
    {
        using var h = new StoreHttpScenario();
        var s1 = await h.Create("/api/v1/stores", new { name = "Store one", address = "Synthetic address" });
        var s2 = await h.Create("/api/v1/stores", new { name = "Store two", address = "Synthetic address" });
        var root1 = "/api/v1/stores/" + s1.GetProperty("id").GetString();
        var root2 = "/api/v1/stores/" + s2.GetProperty("id").GetString();
        var a1 = await h.Create(root1 + "/areas", new { name = "Kitchen" });
        var a2 = await h.Create(root2 + "/areas", new { name = "Kitchen" });
        var a1Id = a1.GetProperty("id").GetString();
        var e = await h.Create(root1 + "/areas/" + a1Id + "/equipment", new { name = "Oven" });
        var badArea = root2 + "/areas/" + a1Id;
        Assert.Equal(HttpStatusCode.NotFound, (await h.Send(HttpMethod.Get, badArea)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Send(HttpMethod.Post, badArea + "/equipment", new { name = "Wrong relation" })).StatusCode);
        var wrongEquipment = root2 + "/areas/" + a2.GetProperty("id").GetString() + "/equipment/" + e.GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.NotFound, (await h.Send(HttpMethod.Get, wrongEquipment)).StatusCode);
        Assert.Equal(5, h.Repository.ConfirmedSaves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task No_credentials_or_machine_key_alone_cannot_create_or_query(bool edgeKey)
    {
        using var h = new StoreHttpScenario();
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post })
            Assert.Equal(HttpStatusCode.Unauthorized, (await h.Send(method, "/api/v1/stores",
                method == HttpMethod.Post ? new { name = "Synthetic", address = "Synthetic" } : null,
                anonymous: true, edgeKey: edgeKey)).StatusCode);
        Assert.Equal(0, h.Repository.ConfirmedSaves);
    }

    [Fact]
    public async Task Invalid_input_forged_owner_and_unbounded_paging_are_rejected()
    {
        using var h = new StoreHttpScenario();
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Send(HttpMethod.Post, "/api/v1/stores",
            new { name = "Synthetic", address = "Synthetic", ownerUserId = h.Other.Id.Value })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Send(HttpMethod.Post, "/api/v1/stores", new { name = " ", address = "Synthetic" })).StatusCode);
        foreach (var query in new[] { "page=0", "pageSize=101", "page=2147483647&pageSize=100" })
            Assert.Equal(HttpStatusCode.BadRequest, (await h.Send(HttpMethod.Get, "/api/v1/stores?" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Send(HttpMethod.Post, "/api/v1/stores/prop-legacy/areas", new { name = "Kitchen" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Send(HttpMethod.Post,
            "/api/v1/stores/store-00000000000000000000000000000000/areas", new { name = "Kitchen" })).StatusCode);
        Assert.Equal(0, h.Repository.ConfirmedSaves);
    }

    [Fact]
    public async Task Paged_lists_are_bounded_and_scoped_even_with_machine_header_and_valid_human()
    {
        using var h = new StoreHttpScenario();
        await h.Create("/api/v1/stores", new { name = "One", address = "Synthetic" });
        await h.Create("/api/v1/stores", new { name = "Two", address = "Synthetic" });
        var firstResponse = await h.Send(HttpMethod.Get, "/api/v1/stores?pageSize=1", edgeKey: true);
        firstResponse.EnsureSuccessStatusCode();
        var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await h.Send(HttpMethod.Get, "/api/v1/stores?page=2&pageSize=1")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(first.GetProperty("items").EnumerateArray());
        Assert.Single(second.GetProperty("items").EnumerateArray());
        Assert.True(first.GetProperty("hasMore").GetBoolean());
        Assert.False(second.GetProperty("hasMore").GetBoolean());
        Assert.NotEqual(first.GetProperty("items")[0].GetProperty("id").GetString(), second.GetProperty("items")[0].GetProperty("id").GetString());
    }
}
