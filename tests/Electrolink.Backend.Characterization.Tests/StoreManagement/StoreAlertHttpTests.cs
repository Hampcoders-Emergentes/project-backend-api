using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

public sealed class StoreAlertHttpTests
{
    [Fact]
    public async Task Store_history_combines_equipment_filters_before_pagination_and_keeps_contract()
    {
        var data = new EquipmentTelemetryScenario(); using var h = new StoreHttpScenario(data);
        var fixture = await StoreAlertCases.Setup(h, data);
        data.Bindings.AddRange(fixture.Links); data.Measurements.AddRange(fixture.Rows.Select(r => r.Measurement));
        data.Alerts.Rows.AddRange(fixture.Rows.Select(r => r.Alert));
        var before = JsonSerializer.Serialize(new { data.Alerts.Rows, data.Measurements, data.Bindings });
        await StoreAlertCases.Queries(h, fixture);
        Assert.Equal(before, JsonSerializer.Serialize(new { data.Alerts.Rows, data.Measurements, data.Bindings }));
    }
    [Fact]
    public async Task Store_history_rejects_invalid_filters_and_incompatible_equipment_without_disclosure()
    {
        var data = new EquipmentTelemetryScenario(); using var h = new StoreHttpScenario(data);
        var fixture = await StoreAlertCases.Setup(h, data);
        await StoreAlertCases.InvalidAndDenied(h, fixture);
        Assert.Empty(data.Alerts.Rows); Assert.Empty(data.Measurements);
    }
    [Fact]
    public async Task Store_history_remains_readable_without_ingestion_authority_after_retirement()
    {
        var data = new EquipmentTelemetryScenario(); using var h = new StoreHttpScenario(data);
        var fixture = await StoreAlertCases.Setup(h, data);
        data.Bindings.AddRange(fixture.Links); data.Alerts.Rows.AddRange(fixture.Rows.Select(r => r.Alert));
        data.Device.Decommission("Synthetic historical fixture"); data.ProfileActive = false;
        await StoreAlertCases.Queries(h, fixture);
    }
}
internal sealed record StoreAlertFixture(string StorePath, string[] EquipmentPaths, List<EquipmentDeviceBinding> Links, List<AlertDateRow> Rows);
internal static class StoreAlertCases
{
    internal static async Task<StoreAlertFixture> Setup(IDemoHttpScenario h, EquipmentTelemetryScenario? inventory = null)
    {
        var first = await HistoryCases.Equipment(h); var storePath = "/api/v1/stores/" + first.Split('/')[4];
        var area = await h.Create(storePath + "/areas", new { name = "Synthetic second area" });
        var secondBase = storePath + "/areas/" + area.GetProperty("id").GetString() + "/equipment";
        var second = await h.Create(secondBase, new { name = "Synthetic second equipment" });
        var sameOwnerOtherStore = await HistoryCases.Equipment(h);
        async Task<JsonElement> Foreign(string path, object body)
        {
            using var response = await h.Send(HttpMethod.Post, path, body, otherUser: true);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        var foreignStore = await Foreign("/api/v1/stores", new { name = "Synthetic foreign store", address = "Synthetic address" });
        var foreignBase = "/api/v1/stores/" + foreignStore.GetProperty("id").GetString();
        var foreignArea = await Foreign(foreignBase + "/areas", new { name = "Synthetic foreign area" });
        var foreignEquipmentBase = foreignBase + "/areas/" + foreignArea.GetProperty("id").GetString() + "/equipment";
        var foreignEquipment = await Foreign(foreignEquipmentBase, new { name = "Synthetic foreign equipment" });
        var paths = new[] { first, secondBase + "/" + second.GetProperty("id").GetString(), sameOwnerOtherStore,
            foreignEquipmentBase + "/" + foreignEquipment.GetProperty("id").GetString() };
        var links = paths.Select((path, i) => EquipmentDeviceBinding.Link(path.Split('/')[8], i == 3 ? h.Other.Id : h.Owner.Id,
            i == 0 && inventory != null ? inventory.Device.Id.Value : "dev-synthetic-m312-" + i,
            i == 0 && inventory != null ? inventory.Device.SerialNumber.Value : "SERIAL-M312-" + i,
            i == 0 && inventory != null ? inventory.Device.SerialNumber.Value : "SERIAL-M312-" + i,
            i == 0 && inventory != null ? inventory.Property.Id.Value : "prop-synthetic-m312-reference", "prof-synthetic-m312-reference")).ToList();
        return new(storePath, paths, links, links.SelectMany(AlertDateCases.Rows).ToList());
    }
    internal static async Task Queries(IDemoHttpScenario h, StoreAlertFixture f)
    {
        var rows = f.Rows.Where(r => f.Links.Take(2).Any(l => l.Id == r.Alert.BindingId)).ToList();
        var expected = rows.Where(r => r.Alert.CreatedAtUtc >= AlertDateCases.At(5) && r.Alert.CreatedAtUtc < AlertDateCases.At(7))
            .OrderByDescending(r => r.Alert.CreatedAtUtc).ThenBy(r => r.Alert.Id, StringComparer.Ordinal).Select(r => r.Alert.Id).ToArray();
        var collected = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            var value = await Page(h, f.StorePath, AlertDateCases.Range + "&pageSize=2&page=" + page);
            collected.AddRange(Ids(value)); Assert.Equal(page < 3, value.GetProperty("hasMore").GetBoolean());
            Assert.Equal(page, value.GetProperty("page").GetInt32()); Assert.Equal(2, value.GetProperty("pageSize").GetInt32());
            Assert.Equal(new[] { "hasMore", "items", "page", "pageSize" }, value.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
            foreach (var item in value.GetProperty("items").EnumerateArray())
            {
                var original = rows.Single(r => r.Alert.Id == item.GetProperty("id").GetString()).Alert;
                Assert.Equal(original.EquipmentId, item.GetProperty("equipmentId").GetString());
                Assert.Equal(original.AcknowledgedByUserId, item.GetProperty("acknowledgedByUserId").GetString());
                Assert.Equal(original.CapturedAtUtc, item.GetProperty("capturedAtUtc").GetDateTime());
                Assert.Equal(new[] { "acknowledgedAtUtc", "acknowledgedByUserId", "anomalyType", "bindingId", "capturedAtUtc", "createdAtUtc", "detectionSeverity", "equipmentId", "id", "readingId", "severity", "status" },
                    item.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
            }
        }
        Assert.Equal(expected, collected);
        var critical = rows.Where(r => r.Alert.CreatedAtUtc >= AlertDateCases.At(5) && r.Alert.CreatedAtUtc < AlertDateCases.At(7)
            && r.Alert.Status == "OPEN" && r.Alert.Severity == "CRITICAL").OrderByDescending(r => r.Alert.CreatedAtUtc).ThenBy(r => r.Alert.Id, StringComparer.Ordinal).Select(r => r.Alert.Id).ToArray();
        var filtered = await Page(h, f.StorePath, AlertDateCases.Range + "&status=OPEN&severity=CRITICAL&pageSize=2&page=2");
        Assert.Equal(critical.Skip(2), Ids(filtered)); Assert.False(filtered.GetProperty("hasMore").GetBoolean());
        var ack = await Page(h, f.StorePath, AlertDateCases.Range + "&status=ACKNOWLEDGED&severity=NON_CRITICAL");
        Assert.Equal(2, Ids(ack).Length); Assert.All(ack.GetProperty("items").EnumerateArray(), a => Assert.Equal(AlertDateCases.At(9), a.GetProperty("acknowledgedAtUtc").GetDateTime()));
        foreach (var path in f.EquipmentPaths.Take(2))
        {
            var equipmentQuery = AlertDateCases.Range + "&equipmentId=" + path.Split('/')[8];
            var store = await Page(h, f.StorePath, equipmentQuery);
            var equipment = await Page(h, path, AlertDateCases.Range);
            Assert.Equal(equipment.GetRawText(), store.GetRawText());
        }
        var legacy = await Page(h, f.StorePath, ""); Assert.Equal(12, Ids(legacy).Length); Assert.Equal(20, legacy.GetProperty("pageSize").GetInt32());
        Assert.Equal(legacy.GetRawText(), (await Page(h, f.StorePath, "?from=&to=")).GetRawText());
        foreach (var q in new[] { "?from=2026-01-02T00:00:00Z&to=2026-01-03T00:00:00Z", AlertDateCases.Range + "&page=4" })
        { var empty = await Page(h, f.StorePath, q); Assert.Empty(Ids(empty)); Assert.False(empty.GetProperty("hasMore").GetBoolean()); }
        var utc = await Page(h, f.StorePath, "?from=2026-01-01T05:00:00Z&to=2026-01-01T07:00:00Z"); Assert.Equal(expected, Ids(utc));
    }
    internal static async Task InvalidAndDenied(IDemoHttpScenario h, StoreAlertFixture f)
    {
        // Reuse M3.11's invalid date cases against the new store route.
        await AlertDateCases.Invalid(h, f.StorePath);
        foreach (var q in new[] { "?status=RESOLVED", "?severity=High", "?page=0", "?pageSize=0", "?pageSize=101", "?page=2147483647&pageSize=100", "?page=bad" })
        { using var response = await h.Send(HttpMethod.Get, f.StorePath + "/alerts" + q); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }
        foreach (var id in new[] { f.EquipmentPaths[2].Split('/')[8], f.EquipmentPaths[3].Split('/')[8], "equipment-00000000000000000000000000000000", "bad", "%20", "" })
        {
            using var response = await h.Send(HttpMethod.Get, f.StorePath + "/alerts?equipmentId=" + id);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); Assert.Equal("{\"message\":\"Resource not found.\"}", await response.Content.ReadAsStringAsync());
        }
        foreach (var path in new[] { f.StorePath, "/api/v1/stores/" + f.EquipmentPaths[3].Split('/')[4], "/api/v1/stores/store-00000000000000000000000000000000" })
        {
            using var response = await h.Send(HttpMethod.Get, path + "/alerts?from=bad", otherUser: path == f.StorePath);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); Assert.Equal("{\"message\":\"Resource not found.\"}", await response.Content.ReadAsStringAsync());
        }
        foreach (var edge in new[] { false, true })
        { using var response = await h.Send(HttpMethod.Get, f.StorePath + "/alerts", anonymous: true, edgeKey: edge); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); }
    }
    private static string[] Ids(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("id").GetString()!).ToArray();
    private static async Task<JsonElement> Page(IDemoHttpScenario h, string path, string query)
    { using var response = await h.Send(HttpMethod.Get, path + "/alerts" + query); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
}
