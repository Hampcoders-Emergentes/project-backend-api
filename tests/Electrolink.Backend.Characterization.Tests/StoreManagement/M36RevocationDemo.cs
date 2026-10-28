using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

public static class M36RevocationDemo
{
    public static Task Run(string manifestPath) => M35PostgresDemo.Run(manifestPath, verifyRevocation: true);

    internal static async Task Check(RealBackendScenario h, string equipment, string deviceId, string serial, JsonElement acknowledgement, Func<Task>? retire = null)
    {
        var alertPath = equipment + "/alerts/" + acknowledgement.GetProperty("id").GetString();
        var before = await Get(h, equipment + "/telemetry");
        var originalAlert = await Get(h, alertPath);
        Assert.Equal("ANOMALY", before.GetProperty("state").GetString());
        Assert.Equal("ACKNOWLEDGED", originalAlert.GetProperty("status").GetString());
        Assert.Equal(acknowledgement.GetRawText(), originalAlert.GetRawText());
        var originalBinding = before.GetProperty("binding").GetRawText();
        string rows, key; string[] allowed; string propertyId;
        using (var pre = h.Scope())
        {
            var authority = pre.ServiceProvider.GetRequiredService<IDeviceLinkAuthority>();
            Assert.NotNull(await authority.Verify(h.Owner.Id, deviceId, serial));
            var ctx = pre.ServiceProvider.GetRequiredService<StoreManagementDbContext>();
            rows = JsonSerializer.Serialize(await ctx.Measurements.AsNoTracking().OrderBy(r => r.ReadingId).ToListAsync());
            Assert.Equal(2, await ctx.Measurements.CountAsync()); Assert.Equal(1, await ctx.Alerts.CountAsync());
            var config = pre.ServiceProvider.GetRequiredService<IConfiguration>();
            key = config["EdgeApi:ApiKey"]!; Assert.True(!string.IsNullOrWhiteSpace(key));
            allowed = config.GetSection("EdgeApi:EquipmentReadings:AllowedDeviceIds").GetChildren().Select(c => c.Value!).ToArray();
            Assert.Contains(serial, allowed);
            var device = await pre.ServiceProvider.GetRequiredService<IIoTDeviceRepository>().FindByIdAsync(IoTDeviceId.From(deviceId));
            Assert.NotNull(device); Assert.Equal(EDeviceStatus.Installed, device.Status);
            propertyId = device.AssignedPropertyId!.Value;
        }
        // Logical retirement only, through the existing application service and real UoW.
        if (retire != null) await retire();
        else
        {
            using var command = h.Scope();
            await command.ServiceProvider.GetRequiredService<IIoTDeviceCommandService>()
                .Handle(new DecommissionDeviceCommand(deviceId, "Synthetic M3.6 logical retirement"));
        }
        var revokedAt = DateTime.UtcNow;
        using (var post = h.Scope())
        {
            var authority = post.ServiceProvider.GetRequiredService<IDeviceLinkAuthority>();
            Assert.Null(await authority.Verify(h.Owner.Id, deviceId, serial));
            var device = await post.ServiceProvider.GetRequiredService<IIoTDeviceRepository>().FindByIdAsync(IoTDeviceId.From(deviceId));
            Assert.NotNull(device); Assert.Equal(EDeviceStatus.Decommissioned, device.Status);
            Assert.Equal(EConnectionStatus.Disconnected, device.ConnectionStatus);
            Assert.Equal(propertyId, device.AssignedPropertyId!.Value); // Retirement is not assignment deletion.
            var property = await post.ServiceProvider.GetRequiredService<IPropertyRepository>().FindByIdAsync(device.AssignedPropertyId);
            Assert.NotNull(property); Assert.DoesNotContain(deviceId, property.InstalledDeviceIds); Assert.False(property.HasActiveIoTMonitoring);
            var config = post.ServiceProvider.GetRequiredService<IConfiguration>();
            Assert.True(key == config["EdgeApi:ApiKey"]); // Never include the key in assertion diagnostics.
            Assert.Equal(allowed, config.GetSection("EdgeApi:EquipmentReadings:AllowedDeviceIds").GetChildren().Select(c => c.Value!).ToArray());
            Assert.Contains(serial, allowed);
        }
        var capture = DateTime.UtcNow; Assert.True(capture >= revokedAt);
        using (var rejected = await h.Send(HttpMethod.Post, "/api/v1/iot/edge/readings", new
        { deviceId = serial, readingId = "m36-after-revocation", timestamp = capture, voltage = 220f, current = 30f,
            powerFactor = 0.5f, frequency = 60f, source = "EDGE" }, anonymous: true, edgeKey: true))
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        var after = await Get(h, equipment + "/telemetry");
        Assert.Equal("DEVICE_UNVERIFIED", after.GetProperty("state").GetString());
        Assert.Equal(before.GetProperty("lastMeasurement").GetRawText(), after.GetProperty("lastMeasurement").GetRawText());
        Assert.Equal(originalBinding, after.GetProperty("binding").GetRawText());
        var binding = await Get(h, equipment + "/device-binding"); Assert.Equal(originalBinding, binding.GetRawText());
        var page = await Get(h, equipment + "/alerts?status=ACKNOWLEDGED"); Assert.Equal(1, page.GetProperty("items").GetArrayLength());
        Assert.Equal(originalAlert.GetRawText(), (await Get(h, alertPath)).GetRawText());
        using (var repeatAck = await h.Send(HttpMethod.Post, alertPath + "/acknowledgement"))
        { Assert.Equal(HttpStatusCode.OK, repeatAck.StatusCode); var ack = await repeatAck.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(originalAlert.GetRawText(), ack.GetRawText()); }
        foreach (var path in new[] { equipment + "/telemetry", alertPath, alertPath + "/acknowledgement" })
        {
            var method = path.EndsWith("/acknowledgement", StringComparison.Ordinal) ? HttpMethod.Post : HttpMethod.Get;
            using var foreign = await h.Send(method, path, otherUser: true); Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            using var machine = await h.Send(method, path, anonymous: true, edgeKey: true); Assert.Equal(HttpStatusCode.Unauthorized, machine.StatusCode);
        }
        using (var final = h.Scope())
        {
            var ctx = final.ServiceProvider.GetRequiredService<StoreManagementDbContext>();
            Assert.Equal(rows, JsonSerializer.Serialize(await ctx.Measurements.AsNoTracking().OrderBy(r => r.ReadingId).ToListAsync()));
            Assert.Equal(2, await ctx.Measurements.CountAsync()); Assert.Equal(1, await ctx.Alerts.CountAsync());
            Assert.False(await ctx.Measurements.AnyAsync(r => r.ReadingId == "m36-after-revocation"));
            Assert.Equal(1, await ctx.DeviceBindings.CountAsync());
            Assert.Null(await final.ServiceProvider.GetRequiredService<IDeviceLinkAuthority>().Verify(h.Owner.Id, deviceId, serial));
            Assert.Equal(0, await final.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.CountAsync());
        }
        Assert.Equal(0, h.ForbiddenCalls);
        Console.WriteLine("PASS M3.6 real logical retirement persisted; unchanged valid Edge key/scope; new reading 403; DEVICE_UNVERIFIED; measurements/detection/binding/alert/ACK unchanged; human access retained and foreign/Edge denied");
    }
    private static async Task<JsonElement> Get(IDemoHttpScenario h, string path)
    { using var r = await h.Send(HttpMethod.Get, path); Assert.Equal(HttpStatusCode.OK, r.StatusCode); return await r.Content.ReadFromJsonAsync<JsonElement>(); }
}
