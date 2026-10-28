using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Planning.Interfaces.ACL;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

public static class M315AssignmentRaceDemo
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
        var gate = new AssignmentRaceGate();
        using var h = new RealBackendScenario(connection, Read("jwtSecret"), Read("edgeKey"), "SYNTHETIC-M315-" + task,
            machineScope: false, includeRetirementEndpoint: true, observeAssetMutations: true, includeAssignmentHandler: true, saveInterceptor: gate);
        using (var empty = h.Scope())
        {
            var ctx = empty.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0, await ctx.Profiles.CountAsync()); Assert.Equal(0, await ctx.IoTDevices.CountAsync());
            Assert.Equal(0, await ctx.ServiceRequests.CountAsync()); Assert.Equal(0, await ctx.ServiceAssignments.CountAsync());
        }
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + "aA1!";
        await h.RegisterAndComplete("m315-client-" + task + "@example.invalid", password, "00000001");
        ClientIdentity client; string firstProperty, secondProperty;
        using (var scope = h.Scope())
        {
            var sp = scope.ServiceProvider; var claims = await sp.GetRequiredService<IProfilesContextFacade>().GetProfileClaimsAsync(h.Owner.Id.Value);
            Assert.True(claims.HasValue); client = ClientIdentity.FromHomeowner(claims.Value.RoleSubjectId!);
            async Task<string> Property() => (await sp.GetRequiredService<IPropertyCommandService>().Handle(new CreatePropertyCommand(
                ClientIdentity.FromHomeowner(client.ClientId), Address.Create("Synthetic", "1", "Synthetic", "Synthetic", "Synthetic", "00000"),
                Geolocation.Create(0, 0, null, "SYNTHETIC"), EPropertyType.Commercial)))!.Id.Value;
            firstProperty = await Property(); secondProperty = await Property();
        }
        await h.RegisterAndCompleteTechnician("m315-first-" + task + "@example.invalid", password, "00000002", other: true);
        string firstRequest, secondRequest, deviceId, spareId;
        using (var seed = h.Scope())
        {
            var sp = seed.ServiceProvider; var profiles = sp.GetRequiredService<IProfilesContextFacade>();
            var firstTech = (await profiles.GetTechnicianIdByUserIdAsync(h.Other.Id.Value))!;
            Assert.NotNull(firstTech);
            async Task<string> Request(string property, string technician)
            {
                var request = ServiceRequest.Initiate(ClientIdentity.FromHomeowner(client.ClientId), false, null);
                request.SelectProperty(PropertyId.From(property), Geolocation.Create(0, 0, null, "SYNTHETIC")); request.SelectCategory(EServiceCategory.Installation);
                request.AddDetails(RequestPreferences.Create("Synthetic allocation", 0, Money.Of(1, ECurrency.Pen), "synthetic", "synthetic", [], ETimePreference.Anytime), false);
                request.Confirm();
                var recipe = RecipeSnapshot.Create(RecipeId.NewId(), "Synthetic installation", EServiceCategory.Installation, [],
                    ServicePricing.Create(Money.Of(0, ECurrency.Pen), Money.Of(1, ECurrency.Pen), Money.Of(1, ECurrency.Pen)),
                    EstimatedDuration.FromMinutes(1), WarrantyPeriod.OfMonths(0), DateTime.UtcNow);
                var assignment = ServiceAssignment.Assign(request.RequestId, TechnicianId.From(technician), recipe,
                    MatchingCriteria.Create(Geolocation.Create(0, 0, null, "SYNTHETIC"), [], false), MatchingScore.Deterministic(1));
                request.MarkAsAssigned(assignment.AssignmentId, TechnicianId.From(technician), recipe);
                request.ClearDomainEvents(); assignment.ClearDomainEvents();
                await sp.GetRequiredService<IServiceRequestRepository>().AddAsync(request);
                await sp.GetRequiredService<IServiceAssignmentRepository>().AddAsync(assignment); await sp.GetRequiredService<IUnitOfWork>().CompleteAsync();
                Assert.True(await sp.GetRequiredService<IInstallationAssignmentReader>().IsAssignedInstallation(request.RequestId.Value, property, technician, client));
                return request.RequestId.Value;
            }
            firstRequest = await Request(firstProperty, firstTech); secondRequest = await Request(secondProperty, firstTech);
            var commands = sp.GetRequiredService<IIoTDeviceCommandService>();
            deviceId = (await commands.Handle(new RegisterTechnicianIoTDeviceCommand(TechnicianId.From(firstTech), "SYNTHETIC-M315-" + task,
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), "synthetic"))).Id.Value;
            spareId = (await commands.Handle(new RegisterTechnicianIoTDeviceCommand(TechnicianId.From(firstTech), "SYNTHETIC-M315-SPARE-" + task,
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), "synthetic-spare"))).Id.Value;
        }
        string Path(string property) => "/api/v1/io-t-devices/" + deviceId + "/assign/" + property;
        var before = await M313DeviceInstallationDemo.Snapshot(h, includeDevice: false, includeProperties: false);
        var secondBefore = await PropertySnapshot(h, secondProperty); var events = h.AssetMutationEvents;
        gate.Arm(deviceId, firstProperty);
        async Task<HttpResponseMessage> First()
        {
            try { return await h.Send(HttpMethod.Post, Path(firstProperty), new { installationRequestId = firstRequest }, otherUser: true); }
            finally { gate.ReleaseLoser(); }
        }
        HttpResponseMessage[] responses;
        try { responses = await Task.WhenAll(First(), h.Send(HttpMethod.Post, Path(secondProperty), new { installationRequestId = secondRequest }, otherUser: true)); }
        finally { gate.Disarm(); }
        using var first = responses[0]; using var second = responses[1];
        Assert.Equal(2, gate.Arrivals); // Both DbContexts reached saving after reading InStock.
        using (var observed = h.Scope())
        {
            var properties = observed.ServiceProvider.GetRequiredService<IPropertyRepository>();
            var a = await properties.FindByIdAsync(PropertyId.From(firstProperty)); var b = await properties.FindByIdAsync(PropertyId.From(secondProperty));
            var memberships = (a!.InstalledDeviceIds.Contains(deviceId) ? 1 : 0) + (b!.InstalledDeviceIds.Contains(deviceId) ? 1 : 0);
            Console.WriteLine("OBSERVED M3.15 race: HTTP=" + (int)first.StatusCode + "/" + (int)second.StatusCode
                + ", property memberships=" + memberships + ", assignment events=" + (h.AssetMutationEvents - events));
        }
        Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("{\"message\":\"Device is not free stock.\"}", await second.Content.ReadAsStringAsync());
        var dto = await first.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(deviceId, dto.GetProperty("id").GetString());
        Assert.Equal((int)EDeviceStatus.Assigned, dto.GetProperty("status").GetInt32()); Assert.Equal(8, dto.EnumerateObject().Count());
        using (var fresh = h.Scope())
        {
            var sp = fresh.ServiceProvider; var device = await sp.GetRequiredService<IIoTDeviceRepository>().FindByIdAsync(IoTDeviceId.From(deviceId));
            var spare = await sp.GetRequiredService<IIoTDeviceRepository>().FindByIdAsync(IoTDeviceId.From(spareId));
            Assert.NotNull(device); Assert.Equal(EDeviceStatus.Assigned, device.Status);
            Assert.Equal(firstProperty, device.AssignedPropertyId!.Value); Assert.Equal(firstRequest, device.InstallationRequestId!.Value);
            Assert.NotNull(spare); Assert.Equal(EDeviceStatus.InStock, spare.Status); Assert.Null(spare.AssignedPropertyId);
            Assert.Contains(deviceId, (await sp.GetRequiredService<IPropertyRepository>().FindByIdAsync(PropertyId.From(firstProperty)))!.InstalledDeviceIds);
        }
        Assert.Equal(secondBefore, await PropertySnapshot(h, secondProperty)); Assert.Equal(events + 1, h.AssetMutationEvents);
        Assert.True(before == await M313DeviceInstallationDemo.Snapshot(h, includeDevice: false, includeProperties: false));
        using (var repeat = await h.Send(HttpMethod.Post, Path(secondProperty), new { installationRequestId = secondRequest }, otherUser: true)) Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        Assert.Equal(secondBefore, await PropertySnapshot(h, secondProperty)); Assert.Equal(events + 1, h.AssetMutationEvents); Assert.Equal(0, h.ForbiddenCalls);
        Console.WriteLine("PASS M3.15: one stock owner, two authorized requests, two stale InStock reads, exactly one assignment/event and HTTP 200/409; losing property unchanged, spare free, fresh scopes, no external calls");
    }
    private static async Task<string> PropertySnapshot(RealBackendScenario h, string property)
    {
        using var scope = h.Scope();
        return JsonSerializer.Serialize(await scope.ServiceProvider.GetRequiredService<IPropertyRepository>().FindByIdAsync(PropertyId.From(property)));
    }
}
