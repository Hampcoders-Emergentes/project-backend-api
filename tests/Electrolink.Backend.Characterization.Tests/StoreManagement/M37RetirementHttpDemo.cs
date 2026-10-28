using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

public static class M37RetirementHttpDemo
{
    public static Task Run(string manifestPath) => M35PostgresDemo.Run(manifestPath, verifyRevocation: true, verifyRetirementHttp: true);
    internal static Task Check(RealBackendScenario h, string equipment, string deviceId, string serial, JsonElement acknowledgement) =>
        M36RevocationDemo.Check(h, equipment, deviceId, serial, acknowledgement, retire: async () =>
        {
            var url = "/api/v1/io-t-devices/" + deviceId + "/decommission";
            async Task Rejected(string name, string path, HttpStatusCode expected, bool other = false, bool anonymous = false, bool edge = false)
            {
                var before = await Snapshot(h, deviceId); var events = h.RetirementEvents.Count;
                using var r = await h.Send(HttpMethod.Post, path, new { reason = "Synthetic M3.7", actorUserId = h.Owner.Id.Value },
                    otherUser: other, anonymous: anonymous, edgeKey: edge);
                var after = await Snapshot(h, deviceId);
                Console.WriteLine("OBSERVED M3.7 " + name + ": HTTP " + (int)r.StatusCode + "; inventory/history unchanged=" + (before == after)
                    + "; retirement events delta=" + (h.RetirementEvents.Count - events));
                Assert.Equal(expected, r.StatusCode);
                Assert.True(before == after, "Rejection changed persisted state");
                Assert.Equal(events, h.RetirementEvents.Count);
            }
            // All negatives precede retirement; a previously retired state cannot mask an authorization bug.
            await Rejected("anonymous", url, HttpStatusCode.Unauthorized, anonymous: true);
            await Rejected("Edge-only", url, HttpStatusCode.Unauthorized, anonymous: true, edge: true);
            await Rejected("foreign-user", url, HttpStatusCode.NotFound, other: true);
            await Rejected("unknown-id", "/api/v1/io-t-devices/dev-m37-does-not-exist/decommission", HttpStatusCode.NotFound);
            using (var accepted = await h.Send(HttpMethod.Post, url, new { reason = "Synthetic M3.7 owner retirement" }))
                Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
            Assert.Equal(1, h.RetirementEvents.Count);
            var retired = await Snapshot(h, deviceId);
            using (var repeated = await h.Send(HttpMethod.Post, url, new { reason = "Synthetic M3.7 repeated retirement" }))
                Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
            Assert.Equal(1, h.RetirementEvents.Count); Assert.True(retired == await Snapshot(h, deviceId));
            Console.WriteLine("PASS M3.7 owner HTTP 204; repeat 204 without another retirement event or persisted change");
        });
    private static async Task<string> Snapshot(RealBackendScenario h, string deviceId)
    {
        using var scope = h.Scope();
        var device = await scope.ServiceProvider.GetRequiredService<IIoTDeviceRepository>().FindByIdAsync(IoTDeviceId.From(deviceId));
        Assert.NotNull(device);
        var property = device.AssignedPropertyId == null ? null : await scope.ServiceProvider.GetRequiredService<IPropertyRepository>().FindByIdAsync(device.AssignedPropertyId);
        var ctx = scope.ServiceProvider.GetRequiredService<StoreManagementDbContext>();
        var state = new { Device = device, Property = property,
            Measurements = await ctx.Measurements.AsNoTracking().OrderBy(r => r.ReadingId).ToListAsync(),
            Alerts = await ctx.Alerts.AsNoTracking().OrderBy(a => a.Id).ToListAsync(),
            Outbox = await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.CountAsync() };
        // Comparison remains in memory; never print rows, credentials, key hashes or this digest.
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state))));
    }
}
