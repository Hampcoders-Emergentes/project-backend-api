using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

public sealed class MeasurementHistoryHttpTests
{
    [Fact]
    public async Task Capture_interval_UTC_boundaries_ties_and_two_pages_preserve_stored_values()
    {
        var data = new EquipmentTelemetryScenario(); using var h = new StoreHttpScenario(data);
        var path = await HistoryCases.Equipment(h);
        await h.Send(HttpMethod.Post, path + "/device-binding", new { deviceId = data.Device.Id.Value, edgeDeviceId = data.Device.SerialNumber.Value });
        data.Measurements.AddRange(HistoryCases.Rows(Assert.Single(data.Bindings)));
        await HistoryCases.Pages(h, path);
        Assert.Equal(6, data.Measurements.Count); Assert.Empty(data.Alerts.Rows);
    }

    [Fact]
    public async Task Foreign_actor_crossed_ancestry_and_machine_are_denied_but_retired_owner_keeps_history()
    {
        var data = new EquipmentTelemetryScenario(); using var h = new StoreHttpScenario(data);
        var path = await HistoryCases.Equipment(h);
        await h.Send(HttpMethod.Post, path + "/device-binding", new { deviceId = data.Device.Id.Value, edgeDeviceId = data.Device.SerialNumber.Value });
        data.Measurements.AddRange(HistoryCases.Rows(Assert.Single(data.Bindings)));
        var second = await HistoryCases.Equipment(h);
        await HistoryCases.Denied(h, path, second);
        data.Device.Decommission("Synthetic history fixture"); data.ProfileActive = false; data.MachineScope = false;
        await HistoryCases.Pages(h, path); // No current device/ingestion authority is needed to read stored history.
        Assert.Equal(6, data.Measurements.Count);
    }

    [Fact]
    public async Task Invalid_range_zone_precision_and_paging_are_bad_requests_without_writes()
    {
        var data = new EquipmentTelemetryScenario(); using var h = new StoreHttpScenario(data);
        var path = await HistoryCases.Equipment(h);
        await HistoryCases.Invalid(h, path);
        Assert.Empty(data.Measurements); Assert.Empty(data.Alerts.Rows);
    }

    [Fact]
    public async Task Authorized_equipment_without_binding_returns_empty_page_with_defaults()
    {
        var data = new EquipmentTelemetryScenario(); using var h = new StoreHttpScenario(data);
        var path = await HistoryCases.Equipment(h);
        using var response = await h.Send(HttpMethod.Get, path + "/measurements" + HistoryCases.Range);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.Equal(1, page.GetProperty("page").GetInt32()); Assert.Equal(20, page.GetProperty("pageSize").GetInt32());
        Assert.False(page.GetProperty("hasMore").GetBoolean());
    }
}

internal static class HistoryCases
{
    internal const string Range = "?from=2026-01-01T00:00:00-05:00&to=2026-01-01T02:00:00-05:00";
    internal static DateTime Capture(int hour) => new(2026, 1, 1, hour, 0, 0, DateTimeKind.Utc);
    internal static IEnumerable<EquipmentMeasurement> Rows(EquipmentDeviceBinding binding)
    {
        // Deliberately reverse equal-capture input order, and receive well after capture.
        foreach (var (id, time) in new[] { ("before", Capture(4)), ("start", Capture(5)), ("tie-z", Capture(6)),
            ("tie-a", Capture(6)), ("end", Capture(7)), ("after", Capture(8)) })
        {
            var row = EquipmentMeasurement.Capture(binding, id, time, 220, 2, 0.9f, 60, "EdgeBufferReplay", Capture(8).AddDays(1));
            if (id == "tie-a") row.RecordDetection("ShortCircuitRisk", "High");
            yield return row;
        }
    }
    internal static async Task<string> Equipment(IDemoHttpScenario h)
    {
        var store = await h.Create("/api/v1/stores", new { name = "Synthetic history", address = "Synthetic" });
        var root = "/api/v1/stores/" + store.GetProperty("id").GetString();
        var area = await h.Create(root + "/areas", new { name = "Synthetic kitchen" });
        var parent = root + "/areas/" + area.GetProperty("id").GetString();
        var equipment = await h.Create(parent + "/equipment", new { name = "Synthetic oven" });
        return parent + "/equipment/" + equipment.GetProperty("id").GetString();
    }
    internal static async Task Pages(IDemoHttpScenario h, string path)
    {
        var first = await Page(h, path, Range + "&pageSize=2&page=1");
        var second = await Page(h, path, Range + "&pageSize=2&page=2");
        Assert.Equal(new[] { "start", "tie-a" }, Ids(first)); Assert.Equal(new[] { "tie-z" }, Ids(second));
        Assert.True(first.GetProperty("hasMore").GetBoolean()); Assert.False(second.GetProperty("hasMore").GetBoolean());
        Assert.Equal(1, first.GetProperty("page").GetInt32()); Assert.Equal(2, second.GetProperty("page").GetInt32());
        Assert.Equal(2, first.GetProperty("pageSize").GetInt32());
        var utc = await Page(h, path, "?from=2026-01-01T05:00:00Z&to=2026-01-01T07:00:00Z&pageSize=2");
        Assert.Equal(first.GetRawText(), utc.GetRawText());
        var positiveOffset = await Page(h, path, "?from=2026-01-01T06:00:00%2B01:00&to=2026-01-01T08:00:00%2B01:00&pageSize=2");
        Assert.Equal(first.GetRawText(), positiveOffset.GetRawText());
        var empty = await Page(h, path, Range + "&page=3&pageSize=2"); Assert.Empty(Ids(empty)); Assert.False(empty.GetProperty("hasMore").GetBoolean());
        var values = first.GetProperty("items").EnumerateArray().ToArray();
        foreach (var value in values)
        {
            Assert.Equal(new[] { "capturedAtUtc", "current", "detectedAnomalyType", "detectedSeverity", "frequency", "powerFactor", "readingId", "receivedAtUtc", "source", "timestampBasis", "voltage" },
                value.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
            Assert.Equal(220f, value.GetProperty("voltage").GetSingle()); Assert.Equal(2f, value.GetProperty("current").GetSingle());
            Assert.Equal(0.9f, value.GetProperty("powerFactor").GetSingle()); Assert.Equal(60f, value.GetProperty("frequency").GetSingle());
            Assert.Equal("EdgeBufferReplay", value.GetProperty("source").GetString()); Assert.Equal("EXPLICIT_OFFSET", value.GetProperty("timestampBasis").GetString());
            Assert.Equal(Capture(8).AddDays(1), value.GetProperty("receivedAtUtc").GetDateTime());
        }
        Assert.Equal(Capture(5), values[0].GetProperty("capturedAtUtc").GetDateTime());
        Assert.Equal(JsonValueKind.Null, values[0].GetProperty("detectedAnomalyType").ValueKind);
        Assert.Equal(Capture(6), values[1].GetProperty("capturedAtUtc").GetDateTime());
        Assert.Equal("ShortCircuitRisk", values[1].GetProperty("detectedAnomalyType").GetString());
        Assert.Equal("High", values[1].GetProperty("detectedSeverity").GetString());
        var fractional = await Page(h, path, "?from=2026-01-01T05:00:00.000001Z&to=2026-01-01T07:00:00Z&pageSize=2");
        Assert.Equal(new[] { "tie-a", "tie-z" }, Ids(fractional));
    }
    internal static async Task Denied(IDemoHttpScenario h, string path, string second)
    {
        var p = path.Split('/'); var s = second.Split('/');
        foreach (var wrong in new[] { path, path.Replace(p[4], s[4], StringComparison.Ordinal),
            path.Replace(p[6], s[6], StringComparison.Ordinal), path.Replace(p[8], s[8], StringComparison.Ordinal) })
        {
            using var response = await h.Send(HttpMethod.Get, wrong + "/measurements" + Range, otherUser: wrong == path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("{\"message\":\"Resource not found.\"}", await response.Content.ReadAsStringAsync());
        }
        foreach (var edge in new[] { false, true })
        {
            using var response = await h.Send(HttpMethod.Get, path + "/measurements" + Range, anonymous: true, edgeKey: edge);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.DoesNotContain("readingId", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }
    }
    internal static async Task Invalid(IDemoHttpScenario h, string path)
    {
        foreach (var query in new[] { "", "?from=2026-01-01T05:00:00Z", Range.Replace("-05:00", "", StringComparison.Ordinal),
            Range.Replace("2026-01-01", "not-a-date", StringComparison.Ordinal),
            "?from=2026-01-02T00:00:00Z&to=2026-01-01T00:00:00Z", "?from=2026-01-01T00:00:00Z&to=2026-01-01T00:00:00Z",
            "?from=2026-01-01T00:00:00Z&to=2026-02-02T00:00:00Z", "?from=2026-01-01T00:00:00.0000001Z&to=2026-01-02T00:00:00Z",
            Range + "&page=0", Range + "&pageSize=0", Range + "&pageSize=101", Range + "&page=2147483647&pageSize=100", Range + "&page=bad" })
        {
            using var response = await h.Send(HttpMethod.Get, path + "/measurements" + query);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.DoesNotContain("readingId", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }
    }
    private static string[] Ids(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("readingId").GetString()!).ToArray();
    private static async Task<JsonElement> Page(IDemoHttpScenario h, string path, string query)
    {
        using var response = await h.Send(HttpMethod.Get, path + "/measurements" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
