using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;
using Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

public static class M39MeasurementHistoryDemo
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
        var serial = "SYNTHETIC-M39-" + task;
        using var h = new RealBackendScenario(connection, Read("jwtSecret"), Read("edgeKey"), serial,
            includeRetirementHandler: true, observeAssetMutations: true);
        using (var empty = h.Scope())
        {
            Assert.Equal(0, await empty.ServiceProvider.GetRequiredService<AppDbContext>().Profiles.CountAsync());
            Assert.Equal(0, await empty.ServiceProvider.GetRequiredService<StoreManagementDbContext>().Measurements.CountAsync());
        }
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + "aA1!";
        await h.RegisterAndComplete("m39-owner-" + task + "@example.invalid", password, "00000001");
        await h.RegisterAndComplete("m39-other-" + task + "@example.invalid", password, "00000002", other: true);
        string deviceId;
        using (var prepare = h.Scope())
        {
            var sp = prepare.ServiceProvider;
            var claims = await sp.GetRequiredService<IProfilesContextFacade>().GetProfileClaimsAsync(h.Owner.Id.Value);
            Assert.True(claims.HasValue);
            var property = await sp.GetRequiredService<IPropertyCommandService>().Handle(new CreatePropertyCommand(ClientIdentity.FromHomeowner(claims.Value.RoleSubjectId!),
                Address.Create("Synthetic", "1", "Synthetic", "Synthetic", "Synthetic", "00000"), Geolocation.Create(0, 0, null, "SYNTHETIC"), EPropertyType.Commercial));
            Assert.NotNull(property);
            var commands = sp.GetRequiredService<IIoTDeviceCommandService>();
            deviceId = (await commands.Handle(new RegisterIoTDeviceCommand(serial, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), "synthetic-m39"))).Id.Value;
            await commands.Handle(new AssignDeviceToPropertyCommand(deviceId, property.Id.Value, "synthetic-installation"));
            await commands.Handle(new RecordDeviceInstallationCommand(deviceId, property.Id.Value, "tech-m39-synthetic-reference", null, "synthetic-m39", DateTime.UtcNow));
        }
        var path = await HistoryCases.Equipment(h); var second = await HistoryCases.Equipment(h);
        await h.Create(path + "/device-binding", new { deviceId, edgeDeviceId = serial });
        using (var seed = h.Scope())
        {
            var ctx = seed.ServiceProvider.GetRequiredService<StoreManagementDbContext>();
            var binding = await ctx.DeviceBindings.AsNoTracking().SingleAsync();
            var rows = HistoryCases.Rows(binding).ToArray(); ctx.Measurements.AddRange(rows);
            var detection = rows.Single(r => r.ReadingId == "tie-a");
            var alert = Alert.Open(new(binding.Id, detection.ReadingId, binding.EquipmentId, h.Owner.Id,
                detection.AnomalyType!, detection.Severity!, detection.CapturedAtUtc, detection.ReceivedAtUtc));
            alert.Acknowledge(h.Owner.Id, DateTime.UtcNow); ctx.Alerts.Add(alert);
            await ctx.SaveChangesAsync(); // Synthetic stored history fixture, not a replay of the ingest/ACK journey.
        }
        await ReadOnly(h, async () =>
        {
            await HistoryCases.Pages(h, path); await HistoryCases.Denied(h, path, second); await HistoryCases.Invalid(h, path);
            var witness = new HistorySqlWitness();
            await using var ctx = new StoreManagementDbContext(new DbContextOptionsBuilder<StoreManagementDbContext>()
                .UseNpgsql(connection).AddInterceptors(witness).Options);
            var repository = new EquipmentTelemetryRepository(ctx); var parts = path.Split('/');
            var page = await repository.History(h.Owner.Id, parts[4], parts[6], parts[8], HistoryCases.Capture(5), HistoryCases.Capture(7), 2, 3, default);
            Assert.Equal(new[] { "tie-z" }, page.Select(r => r.ReadingId));
            Assert.Contains("JOIN", witness.Sql); Assert.Contains("WHERE", witness.Sql); Assert.Contains("ORDER BY", witness.Sql);
            Assert.Contains("LIMIT", witness.Sql); Assert.Contains("OFFSET", witness.Sql); Assert.Contains("COLLATE \"C\"", witness.Sql);
            Assert.Equal(1, witness.Commands);
            Assert.Empty(await repository.History(h.Other.Id, parts[4], parts[6], parts[8], HistoryCases.Capture(5), HistoryCases.Capture(7), 0, 3, default));
            Assert.Empty(await repository.History(h.Owner.Id, second.Split('/')[4], parts[6], parts[8], HistoryCases.Capture(5), HistoryCases.Capture(7), 0, 3, default));
        });
        Console.WriteLine("PASS M3.9 HTTP two pages, UTC half-open capture bounds, tie identity, stored detection independent of ACK; foreign/crossed ancestry/anonymous/Edge denied; invalid parameters rejected; real PostgreSQL JOIN/WHERE/ORDER BY/LIMIT/OFFSET");
        using (var retire = h.Scope())
            await retire.ServiceProvider.GetRequiredService<IIoTDeviceCommandService>().Handle(new DecommissionDeviceCommand(deviceId, "Synthetic history retirement fixture"));
        using (var verify = h.Scope())
        {
            Assert.Null(await verify.ServiceProvider.GetRequiredService<Hampcoders.Electrolink.API.StoreManagement.Application.IDeviceLinkAuthority>().Verify(h.Owner.Id, deviceId, serial));
        }
        await ReadOnly(h, () => HistoryCases.Pages(h, path));
        Assert.Equal(0, h.ForbiddenCalls);
        Console.WriteLine("PASS M3.9 after real logical retirement: same history with ingestion authority revoked; measurements/alerts/inventory/outbox unchanged by queries; no Assets modification notifications or external calls");
    }
    private static async Task ReadOnly(RealBackendScenario h, Func<Task> read)
    {
        var before = await Snapshot(h); var events = h.AssetMutationEvents;
        await read();
        Assert.True(before == await Snapshot(h)); Assert.Equal(events, h.AssetMutationEvents);
    }
    private static async Task<string> Snapshot(RealBackendScenario h)
    {
        using var scope = h.Scope(); var store = scope.ServiceProvider.GetRequiredService<StoreManagementDbContext>();
        var legacy = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var value = new { Measurements = await store.Measurements.AsNoTracking().OrderBy(r => r.BindingId).ThenBy(r => r.ReadingId).ToListAsync(),
            Alerts = await store.Alerts.AsNoTracking().OrderBy(a => a.Id).ToListAsync(), Bindings = await store.DeviceBindings.AsNoTracking().OrderBy(b => b.Id).ToListAsync(),
            Devices = await legacy.IoTDevices.AsNoTracking().OrderBy(d => d.Id).ToListAsync(), Properties = await legacy.Properties.AsNoTracking().OrderBy(p => p.Id).ToListAsync(),
            Outbox = await legacy.OutboxMessages.CountAsync() };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))); // Never log rows or digest.
    }
    private sealed class HistorySqlWitness : DbCommandInterceptor
    {
        internal string Sql { get; private set; } = "";
        internal int Commands { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Sql = command.CommandText; Commands++; return ValueTask.FromResult(result); }
    }
}
