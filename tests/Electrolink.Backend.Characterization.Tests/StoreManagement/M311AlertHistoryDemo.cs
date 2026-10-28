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

public static class M311AlertHistoryDemo
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
        using var h = new RealBackendScenario(connection, Read("jwtSecret"), Read("edgeKey"), "SYNTHETIC-M311-" + task, observeAssetMutations: true);
        using (var empty = h.Scope())
        {
            Assert.Equal(0, await empty.ServiceProvider.GetRequiredService<AppDbContext>().Profiles.CountAsync());
            Assert.Equal(0, await empty.ServiceProvider.GetRequiredService<StoreManagementDbContext>().Alerts.CountAsync());
        }
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + "aA1!";
        await h.RegisterAndComplete("m311-owner-" + task + "@example.invalid", password, "00000001");
        await h.RegisterAndComplete("m311-other-" + task + "@example.invalid", password, "00000002", other: true);
        var path = await HistoryCases.Equipment(h); var second = await HistoryCases.Equipment(h);
        List<AlertDateRow> rows;
        using (var seed = h.Scope())
        {
            var ctx = seed.ServiceProvider.GetRequiredService<StoreManagementDbContext>();
            // Explicit synthetic fixture for a history query: references to legacy inventory are not installation evidence.
            var link = EquipmentDeviceBinding.Link(path.Split('/')[8], h.Owner.Id, "dev-synthetic-m311-" + task,
                "SERIAL-M311-" + task, "SERIAL-M311-" + task, "prop-synthetic-m311-reference", "prof-synthetic-m311-reference");
            rows = AlertDateCases.Rows(link);
            ctx.DeviceBindings.Add(link); ctx.Measurements.AddRange(rows.Select(r => r.Measurement)); ctx.Alerts.AddRange(rows.Select(r => r.Alert));
            await ctx.SaveChangesAsync();
        }
        var before = await Snapshot(h); var events = h.AssetMutationEvents;
        await AlertDateCases.Queries(h, path, rows);
        await AlertDateCases.Invalid(h, path); await AlertDateCases.Denied(h, path, second);
        using (var direct = h.Scope())
        {
            var repo = direct.ServiceProvider.GetRequiredService<IAlertRepository>(); var p = path.Split('/');
            var page = await repo.List(new(h.Owner.Id, p[4], p[6], p[8]), null, null, 2, 3, default, AlertDateCases.At(5), AlertDateCases.At(7));
            Assert.Equal("m311-start", Assert.Single(page).ReadingId);
            Assert.Empty(await repo.List(new(h.Other.Id, p[4], p[6], p[8]), null, null, 0, 3, default, AlertDateCases.At(5), AlertDateCases.At(7)));
        }
        Assert.True(before == await Snapshot(h)); Assert.Equal(events, h.AssetMutationEvents); Assert.Equal(0, h.ForbiddenCalls);
        Console.WriteLine("PASS M3.11 real PostgreSQL alert history: creation interval [from,to), offset normalization, boundary/tie pages, status/severity, unchanged legacy query and empty result");
        Console.WriteLine("PASS M3.11 human ownership/ancestry and direct repository isolation; invalid dates 400; anonymous/Edge 401; inventory/measurements/alerts/ACK/outbox unchanged; no mutation notifications/external calls");
    }
    internal static async Task<string> Snapshot(RealBackendScenario h)
    {
        using var scope = h.Scope(); var ctx = scope.ServiceProvider.GetRequiredService<StoreManagementDbContext>();
        var legacy = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var data = new { Alerts = await ctx.Alerts.AsNoTracking().OrderBy(a => a.Id).ToListAsync(),
            Measurements = await ctx.Measurements.AsNoTracking().OrderBy(a => a.BindingId).ThenBy(a => a.ReadingId).ToListAsync(),
            Bindings = await ctx.DeviceBindings.AsNoTracking().OrderBy(b => b.Id).ToListAsync(), Stores = await ctx.Stores.AsNoTracking().OrderBy(s => s.Id).ToListAsync(),
            Areas = await ctx.Areas.AsNoTracking().OrderBy(a => a.Id).ToListAsync(), Equipment = await ctx.Equipment.AsNoTracking().OrderBy(e => e.Id).ToListAsync(),
            Devices = await legacy.IoTDevices.AsNoTracking().OrderBy(d => d.Id).ToListAsync(), Outbox = await legacy.OutboxMessages.CountAsync() };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data)))); // Never log rows or this digest.
    }
}
