using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Commands;
using Hampcoders.Electrolink.API.IAM.Domain.Repositories;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

public static class M316DeviceRegistrationDemo
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
        var gate = new RegistrationRaceGate("SYNTHETIC-M316-RACE-" + task);
        using var h = new RealBackendScenario(connection, Read("jwtSecret"), Read("edgeKey"), "unused-synthetic-m316",
            machineScope: false, includeRetirementEndpoint: true, observeAssetMutations: true, includeAssignmentHandler: true, includeInstallationHandler: true, saveInterceptor: gate);
        using (var empty = h.Scope())
        {
            var ctx = empty.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0, await ctx.Set<Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates.User>().CountAsync());
            Assert.Equal(0, await ctx.IoTDevices.CountAsync());
        }
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + "aA1!";
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        const string path = "/api/v1/iot-devices";
        const string legacy = "/api/v1/io-t-devices";
        const string stockPath = path + "/my-stock";
        var firstEmail = "m316-first-" + task + "@example.invalid"; var secondEmail = "m316-second-" + task + "@example.invalid";
        await h.RegisterAndComplete("m316-client-" + task + "@example.invalid", password, "00000001");
        ClientIdentity client;
        using (var seed = h.Scope())
        {
            var claims = await seed.ServiceProvider.GetRequiredService<IProfilesContextFacade>().GetProfileClaimsAsync(h.Owner.Id.Value);
            client = ClientIdentity.FromHomeowner(claims!.Value.RoleSubjectId!);
        }
        object Body(string serial, string firmware = "synthetic") => new { serialNumber = serial, plainTextApiKey = key, firmwareVersion = firmware,
            technicianId = "tech-forged-client-value", stockTechnicianId = "tech-forged-client-value" };
        foreach (var route in new[] { path, legacy })
        {
            using var denied = await h.Send(HttpMethod.Post, route, Body("SYNTHETIC-M316-DENIED")); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var anonymous = await h.Send(HttpMethod.Post, route, Body("SYNTHETIC-M316-DENIED"), anonymous: true); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            using var edge = await h.Send(HttpMethod.Post, route, Body("SYNTHETIC-M316-DENIED"), anonymous: true, edgeKey: true); Assert.Equal(HttpStatusCode.Unauthorized, edge.StatusCode);
        }
        // Existing staff creation is technical setup only, to verify there is no Admin/SuperAdmin bypass.
        using (var seed = h.Scope())
        {
            var staff = seed.ServiceProvider.GetRequiredService<IStaffUserCommandService>();
            foreach (var role in new[] { "Admin", "SuperAdmin" })
                await staff.Handle(new CreateStaffUserCommand("m316-" + role.ToLowerInvariant() + "-" + task + "@example.invalid", password, password,
                    role, "Synthetic", role, null));
        }
        foreach (var role in new[] { "admin", "superadmin" })
        {
            await h.Login("m316-" + role + "-" + task + "@example.invalid", password);
            using var denied = await h.Send(HttpMethod.Post, path, Body("SYNTHETIC-M316-DENIED")); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var deniedRead = await h.Send(HttpMethod.Get, stockPath); Assert.Equal(HttpStatusCode.Forbidden, deniedRead.StatusCode);
        }
        await h.RegisterAndCompleteTechnician(firstEmail, password, "00000002", other: true);
        await h.RegisterAndCompleteTechnician(secondEmail, password, "00000003");
        string firstTech, secondTech, propertyId, requestId, legacyId;
        using (var seed = h.Scope())
        {
            var sp = seed.ServiceProvider; var profiles = sp.GetRequiredService<IProfilesContextFacade>();
            firstTech = (await profiles.GetTechnicianIdByUserIdAsync(h.Other.Id.Value))!;
            secondTech = (await profiles.GetTechnicianIdByUserIdAsync(h.Owner.Id.Value))!;
            Assert.NotEqual(firstTech, secondTech);
            propertyId = (await sp.GetRequiredService<IPropertyCommandService>().Handle(new CreatePropertyCommand(client,
                Address.Create("Synthetic", "1", "Synthetic", "Synthetic", "Synthetic", "00000"), Geolocation.Create(0, 0, null, "SYNTHETIC"), EPropertyType.Commercial)))!.Id.Value;
            var request = ServiceRequest.Initiate(ClientIdentity.FromHomeowner(client.ClientId), false, null);
            request.SelectProperty(PropertyId.From(propertyId), Geolocation.Create(0, 0, null, "SYNTHETIC")); request.SelectCategory(EServiceCategory.Installation);
            request.AddDetails(RequestPreferences.Create("Synthetic stock service", 0, Money.Of(1, ECurrency.Pen), "synthetic", "synthetic", [], ETimePreference.Anytime), false); request.Confirm();
            var recipe = RecipeSnapshot.Create(RecipeId.NewId(), "Synthetic installation", EServiceCategory.Installation, [],
                ServicePricing.Create(Money.Of(0, ECurrency.Pen), Money.Of(1, ECurrency.Pen), Money.Of(1, ECurrency.Pen)), EstimatedDuration.FromMinutes(1), WarrantyPeriod.OfMonths(0), DateTime.UtcNow);
            var assignment = ServiceAssignment.Assign(request.RequestId, TechnicianId.From(firstTech), recipe,
                MatchingCriteria.Create(Geolocation.Create(0, 0, null, "SYNTHETIC"), [], false), MatchingScore.Deterministic(1));
            request.MarkAsAssigned(assignment.AssignmentId, TechnicianId.From(firstTech), recipe); request.ClearDomainEvents(); assignment.ClearDomainEvents(); requestId = request.RequestId.Value;
            await sp.GetRequiredService<IServiceRequestRepository>().AddAsync(request); await sp.GetRequiredService<IServiceAssignmentRepository>().AddAsync(assignment);
            await sp.GetRequiredService<IUnitOfWork>().CompleteAsync();
            // Legacy unowned stock is retained and excluded, never attributed to either technician.
            legacyId = (await sp.GetRequiredService<IIoTDeviceCommandService>().Handle(new RegisterIoTDeviceCommand("SYNTHETIC-M316-LEGACY-" + task, key, "synthetic"))).Id.Value;
        }
        var before = await M313DeviceInstallationDemo.Snapshot(h, includeDevice: false, includeProperties: false); var events = h.AssetMutationEvents;
        using var firstStock = await h.Send(HttpMethod.Post, path, Body("SYNTHETIC-M316-FIRST-" + task), otherUser: true);
        Assert.Equal(HttpStatusCode.Created, firstStock.StatusCode);
        using var secondStock = await h.Send(HttpMethod.Post, legacy, Body("SYNTHETIC-M316-SECOND-" + task));
        Assert.Equal(HttpStatusCode.Created, secondStock.StatusCode);
        var foreignId = (await secondStock.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        gate.Arm();
        async Task<HttpResponseMessage> First()
        {
            try { return await h.Send(HttpMethod.Post, path, Body(gate.Serial, "synthetic-first"), otherUser: true); }
            finally { gate.ReleaseLoser(); }
        }
        HttpResponseMessage[] responses;
        try { responses = await Task.WhenAll(First(), h.Send(HttpMethod.Post, path, Body(gate.Serial, "synthetic-second"))); }
        finally { gate.Disarm(); }
        using var first = responses[0]; using var second = responses[1];
        Assert.Equal(2, gate.Arrivals); Assert.Equal(HttpStatusCode.Created, first.StatusCode); Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("{\"message\":\"Device serial number already exists.\"}", await second.Content.ReadAsStringAsync());
        var dto = await first.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(8, dto.EnumerateObject().Count());
        Assert.Equal((int)EDeviceStatus.InStock, dto.GetProperty("status").GetInt32()); Assert.DoesNotContain("key", dto.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var winnerId = dto.GetProperty("id").GetString()!; Assert.Equal(events + 3, h.AssetMutationEvents);
        Assert.Contains("/api/v1/iot-devices/", first.Headers.Location!.ToString());
        using (var readOwn = await h.Send(HttpMethod.Get, first.Headers.Location.PathAndQuery, otherUser: true)) Assert.Equal(HttpStatusCode.OK, readOwn.StatusCode);
        using (var readForeign = await h.Send(HttpMethod.Get, path + "/" + foreignId, otherUser: true)) Assert.Equal(HttpStatusCode.NotFound, readForeign.StatusCode);
        using (var fresh = h.Scope())
        {
            var device = await fresh.ServiceProvider.GetRequiredService<AppDbContext>().IoTDevices.SingleAsync(d => d.SerialNumber == SerialNumber.From(gate.Serial));
            Assert.Equal(winnerId, device.Id.Value); Assert.Equal(firstTech, device.StockTechnicianId!.Value); Assert.True(device.ApiKeyHash.Verify(key));
            Assert.Null(device.AssignedPropertyId); Assert.Null(device.InstallationRequestId);
        }
        using (var duplicate = await h.Send(HttpMethod.Post, path, Body(gate.Serial))) Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using (var invalid = await h.Send(HttpMethod.Post, path, Body(" "))) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        async Task<JsonElement> Stock(bool other, string query = "")
        {
            using var response = await h.Send(HttpMethod.Get, stockPath + query, otherUser: other);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        var own = await Stock(true, "?pageSize=1&technicianId=" + secondTech); Assert.Equal(2, own.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, own.GetProperty("availableCount").GetInt32()); Assert.Single(own.GetProperty("items").EnumerateArray()); Assert.True(own.GetProperty("hasMore").GetBoolean());
        var pageTwo = await Stock(true, "?page=2&pageSize=1"); Assert.False(pageTwo.GetProperty("hasMore").GetBoolean());
        Assert.NotEqual(own.GetProperty("items")[0].GetProperty("id").GetString(), pageTwo.GetProperty("items")[0].GetProperty("id").GetString());
        var otherStock = await Stock(false); Assert.Equal(1, otherStock.GetProperty("totalCount").GetInt32()); Assert.Equal(1, otherStock.GetProperty("availableCount").GetInt32());
        Assert.Equal(foreignId, otherStock.GetProperty("items")[0].GetProperty("id").GetString());
        foreach (var forbiddenId in new[] { foreignId, legacyId })
        {
            using var denied = await h.Send(HttpMethod.Post, legacy + "/" + forbiddenId + "/assign/" + propertyId, new { installationRequestId = requestId }, otherUser: true);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }
        using (var assigned = await h.Send(HttpMethod.Post, legacy + "/" + winnerId + "/assign/" + propertyId, new { installationRequestId = requestId }, otherUser: true))
            Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        own = await Stock(true); Assert.Equal(2, own.GetProperty("totalCount").GetInt32()); Assert.Equal(1, own.GetProperty("availableCount").GetInt32());
        Assert.All(own.GetProperty("items").EnumerateArray(), item => Assert.Equal(4, item.EnumerateObject().Count()));
        Assert.DoesNotContain("key", own.GetRawText(), StringComparison.OrdinalIgnoreCase);
        using (var oldList = await h.Send(HttpMethod.Get, legacy, otherUser: true)) Assert.Equal(2, (await oldList.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
        using (var availableList = await h.Send(HttpMethod.Get, legacy + "/by-status/InStock", otherUser: true)) Assert.Equal(1, (await availableList.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
        using (var installed = await h.Send(HttpMethod.Put, legacy + "/" + winnerId + "/install/" + propertyId,
            new { technicianId = firstTech, firmwareVersion = "synthetic-installed", installedAt = DateTime.UtcNow }, otherUser: true))
            Assert.Equal(HttpStatusCode.OK, installed.StatusCode);
        using (var count = await h.Send(HttpMethod.Get, legacy + "/active-count/" + firstTech, otherUser: true))
        {
            Assert.Equal(HttpStatusCode.OK, count.StatusCode); Assert.Equal(1, (await count.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("count").GetInt32());
        }
        using (var count = await h.Send(HttpMethod.Get, legacy + "/active-count/" + secondTech))
            Assert.Equal(0, (await count.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("count").GetInt32());
        using (var foreignCount = await h.Send(HttpMethod.Get, legacy + "/active-count/" + secondTech, otherUser: true)) Assert.Equal(HttpStatusCode.NotFound, foreignCount.StatusCode);
        using (var suspend = h.Scope())
        {
            var sp = suspend.ServiceProvider; var technician = await sp.GetRequiredService<IUserRepository>().FindByIdAsync(h.Owner.Id);
            Assert.NotNull(technician); technician.Suspend("Synthetic registration denial"); await sp.GetRequiredService<IUnitOfWork>().CompleteAsync();
        }
        foreach (var route in new[] { path, legacy })
        {
            using var denied = await h.Send(HttpMethod.Post, route, Body("SYNTHETIC-M316-SUSPENDED")); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using (var denied = await h.Send(HttpMethod.Get, stockPath)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var fresh = h.Scope())
        {
            var ctx = fresh.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Equal(4, await ctx.IoTDevices.CountAsync());
            var legacyDevice = await ctx.IoTDevices.SingleAsync(d => d.Id == IoTDeviceId.From(legacyId)); Assert.Null(legacyDevice.StockTechnicianId);
            var foreignDevice = await ctx.IoTDevices.SingleAsync(d => d.Id == IoTDeviceId.From(foreignId)); Assert.Equal(EDeviceStatus.InStock, foreignDevice.Status); Assert.Null(foreignDevice.AssignedPropertyId);
        }
        Assert.Equal(events + 5, h.AssetMutationEvents); Assert.Equal(0, h.ForbiddenCalls);
        Assert.True(before == await M313DeviceInstallationDemo.Snapshot(h, includeDevice: false, includeProperties: false));
        Console.WriteLine("PASS M3.16 technician-owned stock: real IAM/Profiles/inventories, actor-derived ownership ignores forged IDs, enabled technicians only; Admin/SuperAdmin/client/Edge/suspended denied, hashed key and legacy DTO");
        Console.WriteLine("PASS M3.16 serial race HTTP 201/409 with one row/event; own SQL counts/pagination and legacy lists isolated; foreign/unowned assignment denied, own assignment decreases available count 2->1, HTTP installation and per-technician active counts 1/0, unrelated state unchanged");
    }
}

// Orders only this test's inserts so both serial lookups complete before the winning transaction commits.
internal sealed class RegistrationRaceGate(string serial) : SaveChangesInterceptor
{
    internal string Serial => serial;
    private readonly TaskCompletionSource<bool> both = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> releaseLoser = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int armed, arrivals;
    internal int Arrivals => Volatile.Read(ref arrivals);
    internal void Arm() => Volatile.Write(ref armed, 1);
    internal void ReleaseLoser() => releaseLoser.TrySetResult(true);
    internal void Disarm() { Volatile.Write(ref armed, 0); ReleaseLoser(); }
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref armed) == 0 || eventData.Context == null) return result;
        var entry = eventData.Context.ChangeTracker.Entries<IoTDevice>().SingleOrDefault(e => e.State == EntityState.Added && e.Entity.SerialNumber.Value == serial);
        if (entry == null) return result;
        var count = Interlocked.Increment(ref arrivals); Assert.InRange(count, 1, 2); if (count == 2) both.TrySetResult(true);
        await both.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        if (entry.Entity.FirmwareVersion != "synthetic-first") await releaseLoser.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        return result;
    }
}
