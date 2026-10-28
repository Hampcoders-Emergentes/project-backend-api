using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

public sealed class AlertHttpTests
{
    [Fact]
    public async Task Detection_replay_access_and_first_ack_preserve_alert_audit_and_monitoring()
    {
        var data = new EquipmentTelemetryScenario(); using var h = new StoreHttpScenario(data);
        var store = await h.Create("/api/v1/stores", new { name = "Synthetic", address = "Synthetic" });
        var root = "/api/v1/stores/" + store.GetProperty("id").GetString();
        var area = await h.Create(root + "/areas", new { name = "Synthetic" });
        var parent = root + "/areas/" + area.GetProperty("id").GetString();
        var equipment = await h.Create(parent + "/equipment", new { name = "Synthetic" });
        var path = parent + "/equipment/" + equipment.GetProperty("id").GetString();
        await h.Send(HttpMethod.Post, path + "/device-binding", new { deviceId = data.Device.Id.Value, edgeDeviceId = data.Device.SerialNumber.Value });
        object Payload(string id, DateTime date, float current) => new { deviceId = data.Device.SerialNumber.Value, readingId = id, timestamp = date,
            voltage = 220f, current, powerFactor = 0.5f, frequency = 60f, source = "EDGE" };
        async Task Ingest(object body) => Assert.Equal(HttpStatusCode.NoContent, (await h.Send(HttpMethod.Post, "/api/v1/iot/edge/readings", body, anonymous: true, edgeKey: true)).StatusCode);
        await Ingest(Payload("old", DateTime.UtcNow.AddHours(-1), 30));
        await Ingest(Payload("normal", DateTime.UtcNow.AddSeconds(-3), 1));
        Assert.Empty(data.Alerts.Rows);
        var critical = Payload("critical", DateTime.UtcNow.AddSeconds(-2), 30);
        await Ingest(critical); await Ingest(critical); Assert.Single(data.Alerts.Rows);
        var list = await Json(h, path + "/alerts?status=OPEN&severity=CRITICAL&pageSize=1");
        var a = list.GetProperty("items")[0]; var alert = path + "/alerts/" + a.GetProperty("id").GetString();
        Assert.Equal("Critical", a.GetProperty("detectionSeverity").GetString()); Assert.Equal("OPEN", a.GetProperty("status").GetString());
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post })
        {
            var url = alert + (method == HttpMethod.Post ? "/acknowledgement" : "");
            Assert.Equal(HttpStatusCode.NotFound, (await h.Send(method, url, otherUser: true)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await h.Send(method, url, anonymous: true, edgeKey: true)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await h.Send(method, url.Replace(area.GetProperty("id").GetString()!, "area-00000000000000000000000000000001", StringComparison.Ordinal))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Send(HttpMethod.Get, path + "/alerts?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Send(HttpMethod.Get, path + "/alerts?status=RESOLVED")).StatusCode);
        var ack = await h.Send(HttpMethod.Post, alert + "/acknowledgement", new { acknowledgedByUserId = h.Other.Id.Value, acknowledgedAtUtc = "2000-01-01T00:00:00Z" });
        Assert.Equal(HttpStatusCode.OK, ack.StatusCode);
        var first = await ack.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(h.Owner.Id.Value, first.GetProperty("acknowledgedByUserId").GetString());
        await h.Send(HttpMethod.Post, alert + "/acknowledgement");
        var after = await Json(h, alert);
        Assert.Equal(first.GetProperty("acknowledgedAtUtc").GetString(), after.GetProperty("acknowledgedAtUtc").GetString());
        Assert.Equal("ACKNOWLEDGED", after.GetProperty("status").GetString());
        Assert.Equal("ANOMALY", (await Json(h, path + "/telemetry")).GetProperty("state").GetString());
        Assert.Equal("ShortCircuitRisk", data.Measurements.Single(r => r.ReadingId == "critical").AnomalyType);
        await Ingest(Payload("another-critical", DateTime.UtcNow.AddSeconds(-1), 30));
        Assert.Equal(2, data.Alerts.Rows.Count);
        var page = await Json(h, path + "/alerts?pageSize=1"); Assert.True(page.GetProperty("hasMore").GetBoolean());
    }
    private static async Task<JsonElement> Json(StoreHttpScenario h, string path)
    { using var response = await h.Send(HttpMethod.Get, path); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
}
