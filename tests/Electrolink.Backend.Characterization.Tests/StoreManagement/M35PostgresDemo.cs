using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
using Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

public static class M35PostgresDemo
{
    public static string LegacySchema()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=offline_m35;Username=synthetic", pg => pg.UseNetTopologySuite()).Options);
        return context.Database.GenerateCreateScript(); // Offline only; never EnsureCreated/Migrate.
    }
    public static async Task Run(string manifestPath, bool verifyRevocation = false, bool verifyRetirementHttp = false)
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        var m = json.RootElement;
        string Read(string key) => m.GetProperty(key).GetString()!;
        var taskId = Read("taskId"); var database = Read("database"); var host = Read("host"); var port = m.GetProperty("port").GetInt32();
        if (!Regex.IsMatch(taskId, "^[0-9a-f]{32}$") || database != "electrolink_m33_" + taskId || host != "127.0.0.1" || port is < 1024 or > 65535)
            throw new InvalidOperationException("Refusing a destination not recognized as task-owned local disposable database");
        var connectionString = new NpgsqlConnectionStringBuilder { Host = host, Port = port, Database = database, Username = "postgres", Password = Read("password"),
            Pooling = false, Timeout = 5, CommandTimeout = 30, IncludeErrorDetail = false }.ConnectionString;
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using (var command = new NpgsqlCommand("SELECT shobj_description(oid,'pg_database') FROM pg_database WHERE datname=current_database()", connection))
            Assert.Equal("electrolink-m33:" + taskId, await command.ExecuteScalarAsync());
        var options = new DbContextOptionsBuilder<StoreManagementDbContext>().UseNpgsql(connectionString).Options;
        await using (var ctx = new StoreManagementDbContext(options))
        { await M33PostgresDemo.CheckSchema(connection, ctx); Assert.Equal(0, await ctx.Stores.CountAsync()); Assert.Equal(0, await ctx.Alerts.CountAsync()); }
        await using (var legacy = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString, pg => pg.UseNetTopologySuite()).Options))
        { Assert.Equal(0, await legacy.Set<Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates.User>().CountAsync()); Assert.Equal(0, await legacy.Profiles.CountAsync()); Assert.Equal(0, await legacy.IoTDevices.CountAsync()); }
        Console.WriteLine("PASS M3.5 task-owned empty database, legacy schema and new module schema available");
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + "aA1!";
        var serial = "SYNTHETIC-M35-" + taskId;
        var ownerEmail = "m35-owner-" + taskId + "@example.invalid"; var otherEmail = "m35-other-" + taskId + "@example.invalid";
        string equipment; JsonElement acknowledgement;
        string ownerId, otherId, profileId, subjectId, deviceId;
        using (var h = new RealBackendScenario(connectionString, Read("jwtSecret"), Read("edgeKey"), serial, includeRetirementHandler: verifyRevocation, includeRetirementEndpoint: verifyRetirementHttp))
        {
            await h.RegisterAndComplete(ownerEmail, password, "00000001");
            await h.RegisterAndComplete(otherEmail, password, "00000002", other: true);
            ownerId = h.Owner.Id.Value; otherId = h.Other.Id.Value;
            using (var scope = h.Scope())
            {
                var facade = scope.ServiceProvider.GetRequiredService<IProfilesContextFacade>();
                var owner = await facade.GetProfileClaimsAsync(ownerId); var other = await facade.GetProfileClaimsAsync(otherId);
                Assert.True(owner.HasValue); Assert.True(other.HasValue); Assert.Equal("Active", owner.Value.ProfileStatus);
                profileId = owner.Value.ProfileId; subjectId = owner.Value.RoleSubjectId!;
                Assert.StartsWith("us-", ownerId); Assert.StartsWith("prof-", profileId); Assert.StartsWith("ho-", subjectId);
                Assert.True(await facade.IsHomeownerActiveAsync(subjectId)); Assert.NotEqual(subjectId, other.Value.RoleSubjectId);
                Assert.NotEqual(ownerId, profileId);
                var portfolio = await scope.ServiceProvider.GetRequiredService<IPropertyPortfolioRepository>().FindByOwnerAsync(ClientIdentity.FromHomeowner(subjectId));
                Assert.NotNull(portfolio); // Verify real ProfileCompleted -> Assets handler did not swallow failure.
                var property = await scope.ServiceProvider.GetRequiredService<IPropertyCommandService>().Handle(new CreatePropertyCommand(
                    ClientIdentity.FromHomeowner(subjectId), Address.Create("Synthetic", "1", "Synthetic", "Synthetic", "Synthetic", "00000"), Geolocation.Create(0, 0, null, "SYNTHETIC"), EPropertyType.Commercial));
                Assert.NotNull(property);
                await scope.ServiceProvider.GetRequiredService<IPropertyPortfolioCommandService>().Handle(new AddPropertyToPortfolioCommand(
                    ClientIdentity.FromHomeowner(subjectId), property.Id, "Synthetic", true, "OwnerOccupied"));
                var commands = scope.ServiceProvider.GetRequiredService<IIoTDeviceCommandService>();
                var device = await commands.Handle(new RegisterIoTDeviceCommand(serial, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), "synthetic-m35"));
                deviceId = device.Id.Value;
                await commands.Handle(new AssignDeviceToPropertyCommand(deviceId, property.Id.Value, "synthetic-m35-installation"));
                await commands.Handle(new RecordDeviceInstallationCommand(deviceId, property.Id.Value, "tech-m35-synthetic-reference", null, "synthetic-m35", DateTime.UtcNow));
                // These are real application commands, not an installation HTTP/technician authorization demonstration.
                Assert.Equal(EDeviceStatus.Installed, device.Status);
            }
            Console.WriteLine("PASS real HTTP sign-up, profile completion, sign-in; persisted profile/subject/portfolio and Assets property/device commands");
            var store = await h.Create("/api/v1/stores", new { name = "M3.5 synthetic store", address = "Synthetic address" });
            var root = "/api/v1/stores/" + store.GetProperty("id").GetString();
            var area = await h.Create(root + "/areas", new { name = "Synthetic kitchen" });
            var parent = root + "/areas/" + area.GetProperty("id").GetString();
            var item = await h.Create(parent + "/equipment", new { name = "Synthetic oven" });
            equipment = parent + "/equipment/" + item.GetProperty("id").GetString();
            var others = await h.CreateForOther();
            using var denied = await h.Send(HttpMethod.Post, others + "/device-binding", new { deviceId, edgeDeviceId = serial }, otherUser: true);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); // Their own equipment, our persisted inventory.
            using var linked = await h.Send(HttpMethod.Post, equipment + "/device-binding", new { deviceId, edgeDeviceId = serial });
            Assert.Equal(HttpStatusCode.Created, linked.StatusCode);
            var date = DateTime.UtcNow.AddSeconds(-2); date = new DateTime(date.Ticks - date.Ticks % 10 + 7, DateTimeKind.Utc);
            var normal = new Payload(serial, "m33-normal", date, 220, 1, 1, 60, "EDGE");
            await Status(h.Send(HttpMethod.Post, "/api/v1/iot/edge/readings", normal, anonymous: true, edgeKey: true), HttpStatusCode.NoContent);
            await Status(h.Send(HttpMethod.Post, "/api/v1/iot/edge/readings", normal, anonymous: true, edgeKey: true), HttpStatusCode.NoContent);
            Assert.Equal("NORMAL", (await Get(h, equipment + "/telemetry")).GetProperty("state").GetString());
            await Status(h.Send(HttpMethod.Post, "/api/v1/iot/edge/readings", normal with { Voltage = 300 }, anonymous: true, edgeKey: true), HttpStatusCode.Conflict);
            await Status(h.Send(HttpMethod.Post, "/api/v1/iot/edge/readings", normal with { ReadingId = "m33-anomaly", Timestamp = DateTime.UtcNow.AddSeconds(-1), Current = 30, PowerFactor = 0.5f }, anonymous: true, edgeKey: true), HttpStatusCode.NoContent);
            acknowledgement = await M33PostgresDemo.ExerciseAlerts(h, equipment, options);
            Assert.Equal(0, h.ForbiddenCalls);
        }
        using (var next = new RealBackendScenario(connectionString, Read("jwtSecret"), Read("edgeKey"), serial, includeRetirementHandler: verifyRevocation, includeRetirementEndpoint: verifyRetirementHttp))
        {
            await next.Login(ownerEmail, password); await next.Login(otherEmail, password, other: true);
            Assert.Equal(ownerId, next.Owner.Id.Value); Assert.Equal(otherId, next.Other.Id.Value);
            var ack = await Get(next, equipment + "/alerts/" + acknowledgement.GetProperty("id").GetString());
            Assert.Equal("ACKNOWLEDGED", ack.GetProperty("status").GetString());
            Assert.Equal(acknowledgement.GetProperty("acknowledgedAtUtc").GetString(), ack.GetProperty("acknowledgedAtUtc").GetString());
            Assert.Equal(ownerId, ack.GetProperty("acknowledgedByUserId").GetString());
            Assert.Equal("ANOMALY", (await Get(next, equipment + "/telemetry")).GetProperty("state").GetString());
            using var scope = next.Scope();
            var authority = scope.ServiceProvider.GetRequiredService<Hampcoders.Electrolink.API.StoreManagement.Application.IDeviceLinkAuthority>();
            Assert.NotNull(await authority.Verify(next.Owner.Id, deviceId, serial)); Assert.Null(await authority.Verify(next.Other.Id, deviceId, serial));
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(2, await ctx.Profiles.CountAsync()); Assert.Equal(1, await ctx.IoTDevices.CountAsync());
            var newContext = scope.ServiceProvider.GetRequiredService<StoreManagementDbContext>();
            Assert.Equal(2, await newContext.Measurements.CountAsync()); Assert.Equal(1, await newContext.Alerts.CountAsync());
            Assert.Equal(0, next.ForbiddenCalls);
            if (verifyRetirementHttp) await M37RetirementHttpDemo.Check(next, equipment, deviceId, serial, acknowledgement);
            else if (verifyRevocation) await M36RevocationDemo.Check(next, equipment, deviceId, serial, acknowledgement);
        }
        Console.WriteLine("PASS M3.5 second real login/server/scopes: profiles, inventory, authority, telemetry and original ACK persist; foreign inventory/access denied; no outbound/legacy calls");
    }
    private sealed record Payload(string DeviceId, string ReadingId, DateTime Timestamp, float Voltage, float Current, float PowerFactor, float Frequency, string Source);
    private static async Task Status(Task<HttpResponseMessage> task, HttpStatusCode status) { using var r = await task; Assert.Equal(status, r.StatusCode); }
    private static async Task<JsonElement> Get(IDemoHttpScenario h, string path) { using var r = await h.Send(HttpMethod.Get, path); Assert.Equal(HttpStatusCode.OK, r.StatusCode); return await r.Content.ReadFromJsonAsync<JsonElement>(); }
}
