using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

public static class M38DeviceReadDemo
{
    public static async Task Run(string manifestPath)
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath)); var m = json.RootElement;
        string Read(string key) => m.GetProperty(key).GetString()!;
        var task = Read("taskId"); var db = Read("database"); var host = Read("host"); var port = m.GetProperty("port").GetInt32();
        if (!Regex.IsMatch(task, "^[0-9a-f]{32}$") || db != "electrolink_m33_" + task || host != "127.0.0.1" || port is < 1024 or > 65535)
            throw new InvalidOperationException("Unrecognized disposable destination");
        var connection = new NpgsqlConnectionStringBuilder { Host = host, Port = port, Database = db, Username = "postgres", Password = Read("password"),
            Pooling = false, Timeout = 5, CommandTimeout = 30, IncludeErrorDetail = false }.ConnectionString;
        await using (var pg = new NpgsqlConnection(connection))
        {
            await pg.OpenAsync(); await using var command = new NpgsqlCommand("SELECT shobj_description(oid,'pg_database') FROM pg_database WHERE datname=current_database()", pg);
            Assert.Equal("electrolink-m33:" + task, await command.ExecuteScalarAsync());
        }
        using var h = new RealBackendScenario(connection, Read("jwtSecret"), Read("edgeKey"), "SYNTHETIC-M38-" + task,
            includeRetirementHandler: true, includeRetirementEndpoint: true, observeAssetMutations: true);
        using (var empty = h.Scope())
        {
            var ctx = empty.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0, await ctx.Profiles.CountAsync()); Assert.Equal(0, await ctx.IoTDevices.CountAsync());
        }
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + "aA1!";
        await h.RegisterAndComplete("m38-owner-" + task + "@example.invalid", password, "00000001");
        await h.RegisterAndComplete("m38-other-" + task + "@example.invalid", password, "00000002", other: true);
        string id, unassigned;
        using (var prepare = h.Scope())
        {
            var sp = prepare.ServiceProvider; var claims = await sp.GetRequiredService<IProfilesContextFacade>().GetProfileClaimsAsync(h.Owner.Id.Value);
            Assert.True(claims.HasValue); Assert.Equal("Active", claims.Value.ProfileStatus);
            var property = await sp.GetRequiredService<IPropertyCommandService>().Handle(new CreatePropertyCommand(ClientIdentity.FromHomeowner(claims.Value.RoleSubjectId!),
                Address.Create("Synthetic", "1", "Synthetic", "Synthetic", "Synthetic", "00000"), Geolocation.Create(0, 0, null, "SYNTHETIC"), EPropertyType.Commercial));
            Assert.NotNull(property);
            var commands = sp.GetRequiredService<IIoTDeviceCommandService>();
            var device = await commands.Handle(new RegisterIoTDeviceCommand("SYNTHETIC-M38-" + task, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), "synthetic-m38"));
            id = device.Id.Value;
            await commands.Handle(new AssignDeviceToPropertyCommand(id, property.Id.Value, "synthetic-installation"));
            await commands.Handle(new RecordDeviceInstallationCommand(id, property.Id.Value, "tech-m38-synthetic-reference", null, "synthetic-m38", DateTime.UtcNow));
            unassigned = (await commands.Handle(new RegisterIoTDeviceCommand("UNASSIGNED-M38-" + task, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), "synthetic-m38"))).Id.Value;
        }
        var url = "/api/v1/io-t-devices/" + id;
        await Check(h, "owner-installed", url, id, HttpStatusCode.OK);
        await Check(h, "anonymous", url, id, HttpStatusCode.Unauthorized, anonymous: true);
        await Check(h, "Edge-only", url, id, HttpStatusCode.Unauthorized, anonymous: true, edge: true);
        await Check(h, "foreign", url + "?actorUserId=" + Uri.EscapeDataString(h.Owner.Id.Value), id, HttpStatusCode.NotFound, other: true);
        await Check(h, "unknown", "/api/v1/io-t-devices/dev-m38-unknown", id, HttpStatusCode.NotFound);
        await Check(h, "unassigned", "/api/v1/io-t-devices/" + unassigned, id, HttpStatusCode.NotFound);
        // Only a fixture for the separately approved read policy. No HTTP retirement or M3.6 journey is repeated.
        using (var prepareRetired = h.Scope())
            await prepareRetired.ServiceProvider.GetRequiredService<IIoTDeviceCommandService>().Handle(new DecommissionDeviceCommand(id, "Synthetic retired read fixture"));
        await Check(h, "owner-retired", url, id, HttpStatusCode.OK);
        await Check(h, "foreign-retired", url, id, HttpStatusCode.NotFound, other: true);
        Assert.Equal(0, h.ForbiddenCalls);
        Console.WriteLine("PASS M3.8 real IAM/Profiles/Assets/PostgreSQL individual GET; exact 8-field DTO; assigned retired still visible to owner; read requests preserve all inventory and publish no Assets modification events");
    }
    internal static async Task Check(RealBackendScenario h, string name, string path, string knownId, HttpStatusCode expected,
        bool other = false, bool anonymous = false, bool edge = false, string label = "M3.8")
    {
        var before = await Snapshot(h); var events = h.AssetMutationEvents;
        using var response = await h.Send(HttpMethod.Get, path, otherUser: other, anonymous: anonymous, edgeKey: edge);
        var body = await response.Content.ReadAsStringAsync(); var after = await Snapshot(h);
        Console.WriteLine("OBSERVED " + label + " " + name + ": HTTP " + (int)response.StatusCode + "; unchanged=" + (before == after) + "; mutation events delta=" + (h.AssetMutationEvents - events));
        Assert.Equal(expected, response.StatusCode); Assert.True(before == after); Assert.Equal(events, h.AssetMutationEvents);
        if (expected == HttpStatusCode.OK)
        {
            var value = JsonDocument.Parse(body).RootElement;
            Assert.Equal(new[] { "assignedPropertyId", "connectionStatus", "firmwareVersion", "id", "installedAt", "lastReadingAt", "serialNumber", "status" },
                value.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
            using var scope = h.Scope();
            var device = await scope.ServiceProvider.GetRequiredService<IIoTDeviceRepository>().FindByIdAsync(IoTDeviceId.From(knownId));
            Assert.NotNull(device); Assert.Equal(device.Id.Value, value.GetProperty("id").GetString());
            Assert.Equal(device.SerialNumber.Value, value.GetProperty("serialNumber").GetString());
            Assert.Equal(device.FirmwareVersion, value.GetProperty("firmwareVersion").GetString());
            Assert.Equal((int)device.Status, value.GetProperty("status").GetInt32());
            Assert.Equal((int)device.ConnectionStatus, value.GetProperty("connectionStatus").GetInt32());
            Assert.Equal(device.AssignedPropertyId!.Value, value.GetProperty("assignedPropertyId").GetString());
            Assert.Equal(device.InstalledAt, value.GetProperty("installedAt").GetDateTime());
            Assert.Equal(JsonValueKind.Null, value.GetProperty("lastReadingAt").ValueKind);
        }
        else
        {
            Assert.DoesNotContain("serialNumber", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("apiKey", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("firmwareVersion", body, StringComparison.OrdinalIgnoreCase);
            if (expected == HttpStatusCode.NotFound)
            {
                using var problem = JsonDocument.Parse(body);
                Assert.Equal(404, problem.RootElement.GetProperty("status").GetInt32());
                Assert.False(problem.RootElement.TryGetProperty("id", out _));
            }
        }
    }
    private static async Task<string> Snapshot(RealBackendScenario h)
    {
        using var scope = h.Scope(); var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var data = new { Devices = await ctx.IoTDevices.AsNoTracking().OrderBy(d => d.Id).ToListAsync(),
            Properties = await ctx.Properties.AsNoTracking().OrderBy(p => p.Id).ToListAsync(), Outbox = await ctx.OutboxMessages.CountAsync() };
        // Never log state, credential fields or this comparison digest.
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data))));
    }
}
