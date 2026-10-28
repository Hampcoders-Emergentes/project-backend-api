using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

public sealed class EquipmentTelemetryHttpTests
{
    [Theory]
    [InlineData("EdgeDevice")]
    [InlineData("EdgeBufferReplay")]
    [InlineData("EDGE")]
    public async Task Authorized_link_edge_reading_duplicate_and_anomaly_are_queryable_without_legacy_effects(string source)
    {
        var data = new EquipmentTelemetryScenario(); using var h = new StoreHttpScenario(data);
        var path = await Equipment(h);
        Assert.Equal("UNKNOWN", (await State(h, path)).GetProperty("state").GetString());
        var response = await h.Send(HttpMethod.Post, path + "/device-binding", new { deviceId = data.Device.Id.Value, edgeDeviceId = data.Device.SerialNumber.Value });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Send(HttpMethod.Get, path + "/device-binding")).StatusCode);
        Assert.Equal("UNKNOWN", (await State(h, path)).GetProperty("state").GetString());
        var thresholds = await h.Send(HttpMethod.Get, "/api/v1/iot/edge/devices/" + data.Device.SerialNumber.Value + "/thresholds", anonymous: true, edgeKey: true);
        Assert.Equal(HttpStatusCode.OK, thresholds.StatusCode);
        var payload = Payload(data.Device.SerialNumber.Value, "m32-reading-1", DateTime.UtcNow.AddSeconds(-2), source);
        Assert.Equal(HttpStatusCode.NoContent, (await Ingest(h, payload)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Ingest(h, payload)).StatusCode);
        Assert.Single(data.Measurements);
        Assert.Equal(HttpStatusCode.Forbidden, (await Ingest(h, Payload(data.Device.Id.Value, "wrong-alias", DateTime.UtcNow.AddSeconds(-1), source))).StatusCode);
        Assert.Single(data.Measurements);
        var normal = await State(h, path);
        Assert.Equal("NORMAL", normal.GetProperty("state").GetString());
        Assert.Equal(220f, normal.GetProperty("lastMeasurement").GetProperty("voltage").GetSingle());
        Assert.Equal(source, normal.GetProperty("lastMeasurement").GetProperty("source").GetString());
        Assert.Equal(data.Device.SerialNumber.Value, normal.GetProperty("binding").GetProperty("edgeDeviceId").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Ingest(h, Payload(data.Device.SerialNumber.Value, "m32-reading-1", payload.Timestamp, source, voltage: 300))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Ingest(h, Payload(data.Device.SerialNumber.Value, "m32-reading-2", DateTime.UtcNow.AddSeconds(-1), source, current: 30, powerFactor: 0.5f))).StatusCode);
        var anomaly = await State(h, path);
        Assert.Equal("ANOMALY", anomaly.GetProperty("state").GetString());
        Assert.Equal("ShortCircuitRisk", anomaly.GetProperty("lastMeasurement").GetProperty("detectedAnomalyType").GetString());
        Assert.Equal(2, data.Measurements.Count);
        Assert.Equal(Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects.EDeviceStatus.Installed, data.Device.Status);
        Assert.Equal(data.Property.Id, data.Device.AssignedPropertyId);
    }

    [Fact]
    public async Task Foreign_actor_invalid_parent_unknown_foreign_and_ambiguous_devices_cannot_link()
    {
        var data = new EquipmentTelemetryScenario(); using var h = new StoreHttpScenario(data); var path = await Equipment(h);
        var body = new { deviceId = data.Device.Id.Value, edgeDeviceId = data.Device.SerialNumber.Value };
        Assert.Equal(HttpStatusCode.NotFound, (await h.Send(HttpMethod.Post, path + "/device-binding", body, otherUser: true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Send(HttpMethod.Get, path + "/telemetry", otherUser: true)).StatusCode);
        foreach (var deviceId in new[] { "dev-m32-unknown", data.ForeignDevice.Id.Value })
            Assert.Equal(HttpStatusCode.Forbidden, (await h.Send(HttpMethod.Post, path + "/device-binding", new { deviceId, edgeDeviceId = data.ForeignDevice.SerialNumber.Value })).StatusCode);
        data.ProfileActive = false;
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Send(HttpMethod.Post, path + "/device-binding", body)).StatusCode);
        data.ProfileActive = true; data.DuplicateAlias = true;
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Send(HttpMethod.Post, path + "/device-binding", body)).StatusCode);
        data.DuplicateAlias = false;
        var wrongParent = path.Replace(path.Split('/')[4], "store-00000000000000000000000000000001", StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Send(HttpMethod.Post, wrongParent + "/device-binding", body)).StatusCode);
        Assert.Empty(data.Bindings);
    }

    [Fact]
    public async Task Contradictory_links_machine_scope_and_revocation_are_denied()
    {
        var data = new EquipmentTelemetryScenario { MachineScope = false }; using var h = new StoreHttpScenario(data); var path = await Equipment(h);
        var body = new { deviceId = data.Device.Id.Value, edgeDeviceId = data.Device.SerialNumber.Value };
        Assert.Equal(HttpStatusCode.Created, (await h.Send(HttpMethod.Post, path + "/device-binding", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await h.Send(HttpMethod.Post, path + "/device-binding", body)).StatusCode);
        var otherEquipment = await Equipment(h);
        Assert.Equal(HttpStatusCode.Conflict, (await h.Send(HttpMethod.Post, otherEquipment + "/device-binding", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Ingest(h, Payload(data.Device.SerialNumber.Value, "x", DateTime.UtcNow.AddSeconds(-1), "EDGE"))).StatusCode);
        Assert.Empty(data.Measurements);
        Assert.Single(data.Bindings);
        var scoped = new EquipmentTelemetryScenario(); using var second = new StoreHttpScenario(scoped); var p = await Equipment(second);
        await second.Send(HttpMethod.Post, p + "/device-binding", new { deviceId = scoped.Device.Id.Value, edgeDeviceId = scoped.Device.SerialNumber.Value });
        Assert.Equal(HttpStatusCode.NoContent, (await Ingest(second, Payload(scoped.Device.SerialNumber.Value, "before-revocation", DateTime.UtcNow.AddSeconds(-1), "EDGE"))).StatusCode);
        scoped.Device.Decommission("synthetic revocation");
        Assert.Equal("DEVICE_UNVERIFIED", (await State(second, p)).GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await Ingest(second, Payload(scoped.Device.SerialNumber.Value, "x", DateTime.UtcNow.AddSeconds(-1), "EDGE"))).StatusCode);
        Assert.Single(scoped.Measurements);
    }

    [Fact]
    public async Task Late_readings_are_not_current_and_missing_credentials_or_invalid_values_do_not_persist()
    {
        var data = new EquipmentTelemetryScenario(); using var h = new StoreHttpScenario(data); var path = await Equipment(h);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Send(HttpMethod.Post, path + "/device-binding", new { deviceId = data.Device.Id.Value, edgeDeviceId = data.Device.SerialNumber.Value }, anonymous: true, edgeKey: true)).StatusCode);
        await h.Send(HttpMethod.Post, path + "/device-binding", new { deviceId = data.Device.Id.Value, edgeDeviceId = data.Device.SerialNumber.Value });
        var stale = Payload(data.Device.SerialNumber.Value, "stale", DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-1), DateTimeKind.Unspecified), "EdgeBufferReplay");
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Send(HttpMethod.Post, "/api/v1/iot/edge/readings", stale, anonymous: true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Ingest(h, stale)).StatusCode);
        var state = await State(h, path);
        Assert.Equal("STALE", state.GetProperty("state").GetString());
        Assert.Equal("EDGE_UTC_ASSUMED", state.GetProperty("lastMeasurement").GetProperty("timestampBasis").GetString());
        Assert.True(data.Measurements[0].ReceivedAtUtc > data.Measurements[0].CapturedAtUtc);
        Assert.Equal(HttpStatusCode.BadRequest, (await Ingest(h, Payload(data.Device.SerialNumber.Value, "bad", DateTime.UtcNow.AddSeconds(-1), "EDGE", current: -1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Ingest(h, Payload("dev-m32-scope-without-binding", "bad", DateTime.UtcNow.AddSeconds(-1), "EDGE"))).StatusCode);
        Assert.Single(data.Measurements);
    }
    private static async Task<string> Equipment(StoreHttpScenario h)
    {
        var s = await h.Create("/api/v1/stores", new { name = "Synthetic", address = "Synthetic" });
        var root = "/api/v1/stores/" + s.GetProperty("id").GetString();
        var a = await h.Create(root + "/areas", new { name = "Kitchen" });
        var area = root + "/areas/" + a.GetProperty("id").GetString();
        var e = await h.Create(area + "/equipment", new { name = "Oven" });
        return area + "/equipment/" + e.GetProperty("id").GetString();
    }
    private static async Task<JsonElement> State(StoreHttpScenario h, string path)
    {
        var response = await h.Send(HttpMethod.Get, path + "/telemetry"); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static Task<HttpResponseMessage> Ingest(StoreHttpScenario h, object payload) => h.Send(HttpMethod.Post, "/api/v1/iot/edge/readings", payload, anonymous: true, edgeKey: true);
    private sealed record ReadingPayload(string DeviceId, string ReadingId, DateTime Timestamp, float Voltage, float Current, float PowerFactor, float Frequency, string Source, float CurrentIna);
    private static ReadingPayload Payload(string id, string reading, DateTime time, string source, float voltage = 220, float current = 1, float powerFactor = 1) => new(id, reading, time, voltage, current, powerFactor, 60, source, 0.5f);
}
