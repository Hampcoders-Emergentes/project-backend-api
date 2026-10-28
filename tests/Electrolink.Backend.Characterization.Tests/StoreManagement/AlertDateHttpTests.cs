using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

public sealed class AlertDateHttpTests
{
    [Fact]
    public async Task Creation_interval_offsets_pages_and_existing_filters_compose_without_changing_legacy_query()
    {
        var data = new EquipmentTelemetryScenario(); using var h = new StoreHttpScenario(data);
        var path = await HistoryCases.Equipment(h);
        var link = EquipmentDeviceBinding.Link(path.Split('/')[8], h.Owner.Id, data.Device.Id.Value, data.Device.SerialNumber.Value,
            data.Device.SerialNumber.Value, data.Property.Id.Value, "prof-synthetic-m311");
        data.Bindings.Add(link);
        var rows = AlertDateCases.Rows(link); data.Measurements.AddRange(rows.Select(r => r.Measurement)); data.Alerts.Rows.AddRange(rows.Select(r => r.Alert));
        var before = JsonSerializer.Serialize(data.Alerts.Rows);
        await AlertDateCases.Queries(h, path, rows);
        Assert.Equal(before, JsonSerializer.Serialize(data.Alerts.Rows)); Assert.Equal(6, data.Measurements.Count);
    }
    [Fact]
    public async Task Invalid_or_unpaired_date_filters_are_rejected()
    {
        var data = new EquipmentTelemetryScenario(); using var h = new StoreHttpScenario(data);
        var path = await HistoryCases.Equipment(h);
        await AlertDateCases.Invalid(h, path); Assert.Empty(data.Alerts.Rows); Assert.Empty(data.Measurements);
    }
    [Fact]
    public async Task Date_query_keeps_ownership_ancestry_human_only_and_retired_history_access()
    {
        var data = new EquipmentTelemetryScenario(); using var h = new StoreHttpScenario(data);
        var path = await HistoryCases.Equipment(h); var second = await HistoryCases.Equipment(h);
        var link = EquipmentDeviceBinding.Link(path.Split('/')[8], h.Owner.Id, data.Device.Id.Value, data.Device.SerialNumber.Value,
            data.Device.SerialNumber.Value, data.Property.Id.Value, "prof-synthetic-m311");
        var rows = AlertDateCases.Rows(link); data.Bindings.Add(link); data.Alerts.Rows.AddRange(rows.Select(r => r.Alert));
        await AlertDateCases.Denied(h, path, second);
        data.Device.Decommission("Synthetic historical query fixture"); data.ProfileActive = false;
        await AlertDateCases.Queries(h, path, rows);
    }
}
internal sealed record AlertDateRow(EquipmentMeasurement Measurement, Alert Alert);
internal static class AlertDateCases
{
    internal const string Range = "?from=2026-01-01T00:00:00-05:00&to=2026-01-01T02:00:00-05:00";
    internal static DateTime At(int hour) => new(2026, 1, 1, hour, 0, 0, DateTimeKind.Utc);
    internal static List<AlertDateRow> Rows(EquipmentDeviceBinding link)
    {
        var result = new List<AlertDateRow>();
        foreach (var (id, hour) in new[] { ("before", 4), ("start", 5), ("tie-a", 6), ("tie-b", 6), ("end", 7), ("after", 8) })
        {
            var capture = At(0).AddDays(-1); // Every capture is OUTSIDE the creation interval.
            var measurement = EquipmentMeasurement.Capture(link, "m311-" + id, capture, 220, 30, 0.5f, 60, "EdgeBufferReplay", At(hour));
            var severity = id == "tie-a" ? "High" : "Critical"; measurement.RecordDetection("ShortCircuitRisk", severity);
            var alert = Alert.Open(new(link.Id, measurement.ReadingId, link.EquipmentId, link.OwnerUserId, "ShortCircuitRisk", severity, capture, At(hour)));
            if (id == "tie-a") alert.Acknowledge(link.OwnerUserId, At(9));
            result.Add(new(measurement, alert));
        }
        return result;
    }
    internal static async Task Queries(IDemoHttpScenario h, string path, List<AlertDateRow> rows)
    {
        var expected = rows.Where(r => r.Alert.CreatedAtUtc >= At(5) && r.Alert.CreatedAtUtc < At(7))
            .OrderByDescending(r => r.Alert.CreatedAtUtc).ThenBy(r => r.Alert.Id, StringComparer.Ordinal).Select(r => r.Alert.Id).ToArray();
        var first = await Page(h, path, Range + "&pageSize=2"); var second = await Page(h, path, Range + "&pageSize=2&page=2");
        Assert.Equal(expected.Take(2), Ids(first)); Assert.Equal(expected.Skip(2), Ids(second));
        Assert.True(first.GetProperty("hasMore").GetBoolean()); Assert.False(second.GetProperty("hasMore").GetBoolean());
        Assert.Equal(1, first.GetProperty("page").GetInt32()); Assert.Equal(2, second.GetProperty("page").GetInt32()); Assert.Equal(2, first.GetProperty("pageSize").GetInt32());
        var utc = await Page(h, path, "?from=2026-01-01T05:00:00Z&to=2026-01-01T07:00:00Z&pageSize=2"); Assert.Equal(first.GetRawText(), utc.GetRawText());
        var positive = await Page(h, path, "?from=2026-01-01T06:00:00%2B01:00&to=2026-01-01T08:00:00%2B01:00&pageSize=2"); Assert.Equal(first.GetRawText(), positive.GetRawText());
        var critical = await Page(h, path, Range + "&status=OPEN&severity=CRITICAL");
        Assert.Equal(2, Ids(critical).Length); Assert.All(critical.GetProperty("items").EnumerateArray(), a => { Assert.Equal("OPEN", a.GetProperty("status").GetString()); Assert.Equal("CRITICAL", a.GetProperty("severity").GetString()); });
        var acknowledged = await Page(h, path, Range + "&status=ACKNOWLEDGED&severity=NON_CRITICAL"); Assert.Single(Ids(acknowledged));
        Assert.Equal("High", acknowledged.GetProperty("items")[0].GetProperty("detectionSeverity").GetString());
        Assert.Equal(At(9), acknowledged.GetProperty("items")[0].GetProperty("acknowledgedAtUtc").GetDateTime());
        var legacy = await Page(h, path, ""); Assert.Equal(6, Ids(legacy).Length); Assert.Equal(20, legacy.GetProperty("pageSize").GetInt32());
        var empty = await Page(h, path, "?from=2026-01-02T00:00:00Z&to=2026-01-03T00:00:00Z"); Assert.Empty(Ids(empty)); Assert.False(empty.GetProperty("hasMore").GetBoolean());
        var subsecond = await Page(h, path, "?from=2026-01-01T05:00:00.000001Z&to=2026-01-01T07:00:00Z"); Assert.Equal(2, Ids(subsecond).Length);
        foreach (var item in first.GetProperty("items").EnumerateArray())
        {
            Assert.Equal(new[] { "acknowledgedAtUtc", "acknowledgedByUserId", "anomalyType", "bindingId", "capturedAtUtc", "createdAtUtc", "detectionSeverity", "equipmentId", "id", "readingId", "severity", "status" },
                item.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
            Assert.Equal(At(0).AddDays(-1), item.GetProperty("capturedAtUtc").GetDateTime()); // Date filter uses creation, not capture or ACK.
        }
    }
    internal static async Task Invalid(IDemoHttpScenario h, string path)
    {
        foreach (var query in new[] { "?from=2026-01-01T00:00:00Z", "?to=2026-01-01T00:00:00Z", "?from=&to=2026-01-02T00:00:00Z",
            "?from=2026-01-01T00:00:00&to=2026-01-02T00:00:00Z", "?from=bad&to=bad",
            "?from=2026-01-02T00:00:00Z&to=2026-01-01T00:00:00Z", "?from=2026-01-01T00:00:00Z&to=2026-01-01T00:00:00Z",
            "?from=2026-01-01T00:00:00Z&to=2026-02-02T00:00:00Z", "?from=2026-01-01T00:00:00.0000001Z&to=2026-01-02T00:00:00Z" })
        {
            using var response = await h.Send(HttpMethod.Get, path + "/alerts" + query); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
    internal static async Task Denied(IDemoHttpScenario h, string path, string second)
    {
        var p = path.Split('/'); var s = second.Split('/');
        foreach (var wrong in new[] { path, path.Replace(p[4], s[4], StringComparison.Ordinal), path.Replace(p[6], s[6], StringComparison.Ordinal), path.Replace(p[8], s[8], StringComparison.Ordinal) })
        {
            using var response = await h.Send(HttpMethod.Get, wrong + "/alerts" + Range, otherUser: wrong == path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); Assert.Equal("{\"message\":\"Resource not found.\"}", await response.Content.ReadAsStringAsync());
        }
        foreach (var edge in new[] { false, true })
        { using var response = await h.Send(HttpMethod.Get, path + "/alerts" + Range, anonymous: true, edgeKey: edge); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); }
    }
    private static string[] Ids(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("id").GetString()!).ToArray();
    private static async Task<JsonElement> Page(IDemoHttpScenario h, string path, string query)
    { using var response = await h.Send(HttpMethod.Get, path + "/alerts" + query); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
}
