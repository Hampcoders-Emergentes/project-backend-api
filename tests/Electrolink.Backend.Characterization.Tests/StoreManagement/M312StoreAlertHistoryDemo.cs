using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

public static class M312StoreAlertHistoryDemo
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
        using var h = new RealBackendScenario(connection, Read("jwtSecret"), Read("edgeKey"), "SYNTHETIC-M312-" + task,
            machineScope: false, observeAssetMutations: true);
        using (var empty = h.Scope())
        {
            var legacy = empty.ServiceProvider.GetRequiredService<AppDbContext>();
            var ctx = empty.ServiceProvider.GetRequiredService<StoreManagementDbContext>();
            Assert.Equal(0, await legacy.Profiles.CountAsync()); Assert.Equal(0, await legacy.IoTDevices.CountAsync());
            Assert.Equal(0, await ctx.Alerts.CountAsync()); Assert.Equal(0, await ctx.Stores.CountAsync());
            Assert.Equal(0, await ctx.Measurements.CountAsync()); Assert.Equal(0, await ctx.DeviceBindings.CountAsync());
        }
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + "aA1!";
        await h.RegisterAndComplete("m312-owner-" + task + "@example.invalid", password, "00000001");
        await h.RegisterAndComplete("m312-other-" + task + "@example.invalid", password, "00000002", other: true);
        var fixture = await StoreAlertCases.Setup(h);
        using (var seed = h.Scope())
        {
            var ctx = seed.ServiceProvider.GetRequiredService<StoreManagementDbContext>();
            // History fixture only: legacy device/property/profile references are synthetic, not inventory or ingestion proof.
            ctx.DeviceBindings.AddRange(fixture.Links); ctx.Measurements.AddRange(fixture.Rows.Select(r => r.Measurement));
            ctx.Alerts.AddRange(fixture.Rows.Select(r => r.Alert)); await ctx.SaveChangesAsync();
        }
        var before = await M311AlertHistoryDemo.Snapshot(h); var events = h.AssetMutationEvents;
        await StoreAlertCases.Queries(h, fixture); await StoreAlertCases.InvalidAndDenied(h, fixture);
        using (var direct = h.Scope())
        {
            var repo = direct.ServiceProvider.GetRequiredService<IAlertRepository>(); var store = fixture.StorePath.Split('/')[4];
            var page = await repo.ListStore(new(h.Owner.Id, store), "OPEN", "CRITICAL", 2, 3, default, AlertDateCases.At(5), AlertDateCases.At(7));
            var expected = fixture.Rows.Where(r => fixture.Links.Take(2).Any(l => l.Id == r.Alert.BindingId)
                && r.Alert.CreatedAtUtc >= AlertDateCases.At(5) && r.Alert.CreatedAtUtc < AlertDateCases.At(7)
                && r.Alert.Status == "OPEN" && r.Alert.Severity == "CRITICAL")
                .OrderByDescending(r => r.Alert.CreatedAtUtc).ThenBy(r => r.Alert.Id, StringComparer.Ordinal).Skip(2).Select(r => r.Alert.Id);
            Assert.Equal(expected, page.Select(a => a.Id));
            Assert.Empty(await repo.ListStore(new(h.Other.Id, store), null, null, 0, 20, default));
            Assert.Empty(await repo.ListStore(new(h.Owner.Id, store, fixture.EquipmentPaths[2].Split('/')[8]), null, null, 0, 20, default));
            Assert.Empty(await repo.ListStore(new(h.Owner.Id, store, fixture.EquipmentPaths[3].Split('/')[8]), null, null, 0, 20, default));
        }
        Assert.True(before == await M311AlertHistoryDemo.Snapshot(h)); Assert.Equal(events, h.AssetMutationEvents); Assert.Equal(0, h.ForbiddenCalls);
        Console.WriteLine("PASS M3.12 real PostgreSQL store alert history: two equipment/areas, exclusion of other stores (same and foreign owner), optional equipment, creation/status/severity before pagination, ties/pages/empty and exact DTO");
        Console.WriteLine("PASS M3.12 invalid filters 400, incompatible equipment and foreign/missing store generic 404, anonymous/Edge 401; direct repository isolation; fresh scopes; alerts/ACK/measurements/bindings/inventory/outbox unchanged; no external calls");
    }
}
