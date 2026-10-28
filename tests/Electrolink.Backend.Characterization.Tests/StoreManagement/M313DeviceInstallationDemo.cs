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

public static class M313DeviceInstallationDemo
{
    public static async Task Run(string manifestPath, bool assignOverHttp = false)
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
        using var h = new RealBackendScenario(connection, Read("jwtSecret"), Read("edgeKey"), "SYNTHETIC-M313-" + task,
            machineScope: false, includeRetirementEndpoint: true, observeAssetMutations: true, includeInstallationHandler: true, includeAssignmentHandler: assignOverHttp);
        using (var empty = h.Scope())
        {
            var ctx = empty.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0, await ctx.Profiles.CountAsync()); Assert.Equal(0, await ctx.IoTDevices.CountAsync());
            Assert.Equal(0, await ctx.ServiceRequests.CountAsync()); Assert.Equal(0, await ctx.ServiceAssignments.CountAsync());
        }
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + "aA1!";
        await h.RegisterAndComplete("m313-client-" + task + "@example.invalid", password, "00000001");
        ClientIdentity client; string propertyId, wrongPropertyId;
        using (var pre = h.Scope())
        {
            var profiles = pre.ServiceProvider.GetRequiredService<IProfilesContextFacade>();
            var claims = await profiles.GetProfileClaimsAsync(h.Owner.Id.Value); Assert.True(claims.HasValue);
            client = ClientIdentity.FromHomeowner(claims.Value.RoleSubjectId!);
            var properties = pre.ServiceProvider.GetRequiredService<IPropertyCommandService>();
            async Task<string> Property() => (await properties.Handle(new CreatePropertyCommand(ClientIdentity.FromHomeowner(client.ClientId),
                Address.Create("Synthetic", "1", "Synthetic", "Synthetic", "Synthetic", "00000"),
                Geolocation.Create(0, 0, null, "SYNTHETIC"), EPropertyType.Commercial)))!.Id.Value;
            propertyId = await Property(); wrongPropertyId = await Property();
        }
        await h.RegisterAndCompleteTechnician("m313-assigned-" + task + "@example.invalid", password, "00000002", other: true);
        await h.RegisterAndCompleteTechnician("m313-unrelated-" + task + "@example.invalid", password, "00000003");
        string technician, foreignTechnician, deviceId; string? spareDeviceId = null; RequestId requestId; AssignmentId assignmentId; RecipeSnapshot recipe;
        using (var seed = h.Scope())
        {
            var sp = seed.ServiceProvider; var profiles = sp.GetRequiredService<IProfilesContextFacade>();
            technician = (await profiles.GetTechnicianIdByUserIdAsync(h.Other.Id.Value))!;
            foreignTechnician = (await profiles.GetTechnicianIdByUserIdAsync(h.Owner.Id.Value))!;
            Assert.NotNull(technician); Assert.NotEqual(technician, foreignTechnician);
            Assert.NotNull(await sp.GetRequiredService<ITechnicianInventoryRepository>().FindByTechnicianIdAsync(TechnicianId.From(technician)));
            var request = ServiceRequest.Initiate(client, false, null);
            request.SelectProperty(PropertyId.From(propertyId), Geolocation.Create(0, 0, null, "SYNTHETIC")); request.SelectCategory(EServiceCategory.Installation);
            request.AddDetails(RequestPreferences.Create("Synthetic installation", 0, Money.Of(1, ECurrency.Pen), "synthetic", "synthetic", [], ETimePreference.Anytime), false);
            request.Confirm(); requestId = request.RequestId;
            recipe = RecipeSnapshot.Create(RecipeId.NewId(), "Synthetic installation metadata", EServiceCategory.Installation, [],
                ServicePricing.Create(Money.Of(0, ECurrency.Pen), Money.Of(1, ECurrency.Pen), Money.Of(1, ECurrency.Pen)),
                EstimatedDuration.FromMinutes(1), WarrantyPeriod.OfMonths(0), DateTime.UtcNow);
            var assignment = ServiceAssignment.Assign(request.RequestId, TechnicianId.From(technician), recipe,
                MatchingCriteria.Create(Geolocation.Create(0, 0, null, "SYNTHETIC"), [], false), MatchingScore.Deterministic(1)); assignmentId = assignment.AssignmentId;
            request.MarkAsAssigned(assignment.AssignmentId, TechnicianId.From(technician), recipe);
            // Technical preconditions through domain/repositories/UoW, without publishing scheduling/matching events.
            request.ClearDomainEvents(); assignment.ClearDomainEvents();
            await sp.GetRequiredService<IServiceRequestRepository>().AddAsync(request);
            await sp.GetRequiredService<IServiceAssignmentRepository>().AddAsync(assignment); await sp.GetRequiredService<IUnitOfWork>().CompleteAsync();
            var commands = sp.GetRequiredService<IIoTDeviceCommandService>();
            if (assignOverHttp)
                spareDeviceId = (await commands.Handle(new RegisterTechnicianIoTDeviceCommand(TechnicianId.From(technician), "SYNTHETIC-M314-SPARE-" + task,
                    Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), "synthetic-spare"))).Id.Value;
            var device = await commands.Handle(new RegisterTechnicianIoTDeviceCommand(TechnicianId.From(technician), "SYNTHETIC-M313-" + task, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), "synthetic-before"));
            deviceId = device.Id.Value;
            if (!assignOverHttp) await commands.Handle(new AssignDeviceToPropertyCommand(deviceId, propertyId, request.RequestId.Value));
        }
        if (assignOverHttp) await M314DeviceAssignmentDemo.Check(h, deviceId, spareDeviceId!, propertyId, wrongPropertyId, requestId);
        var path = "/api/v1/io-t-devices/" + deviceId + "/install/" + propertyId;
        var at = DateTime.UtcNow; at = new DateTime(at.Ticks - at.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc);
        object Payload(string tech) => new { technicianId = tech, firmwareVersion = "synthetic-after", installedAt = at };
        async Task Denied(string url, object payload, HttpStatusCode status, bool other = true, bool anonymous = false, bool edge = false)
        {
            var before = await Snapshot(h); var events = h.AssetMutationEvents;
            using var response = await h.Send(HttpMethod.Put, url, payload, otherUser: other, anonymous: anonymous, edgeKey: edge);
            Assert.Equal(status, response.StatusCode);
            if (status == HttpStatusCode.NotFound) Assert.Equal("{\"message\":\"Resource not found.\"}", await response.Content.ReadAsStringAsync());
            Assert.True(before == await Snapshot(h)); Assert.Equal(events, h.AssetMutationEvents);
        }
        await Denied(path, Payload(foreignTechnician), HttpStatusCode.NotFound, other: false);
        await Denied(path, Payload(technician), HttpStatusCode.Unauthorized, anonymous: true);
        await Denied(path, Payload(technician), HttpStatusCode.Unauthorized, anonymous: true, edge: true);
        await Denied(path, Payload(foreignTechnician), HttpStatusCode.NotFound);
        await Denied(path.Replace(propertyId, wrongPropertyId, StringComparison.Ordinal), Payload(technician), HttpStatusCode.NotFound);
        await Denied(path.Replace(deviceId, "dev-synthetic-m313-missing", StringComparison.Ordinal), Payload(technician), HttpStatusCode.NotFound);
        using (var scope = h.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IServiceRequestRepository>(); var request = (await repo.FindByIdAsync(requestId))!;
            request.Reactivate(); request.ClearDomainEvents(); repo.Update(request); await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CompleteAsync();
        }
        await Denied(path, Payload(technician), HttpStatusCode.NotFound);
        using (var scope = h.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IServiceRequestRepository>(); var request = (await repo.FindByIdAsync(requestId))!;
            request.MarkAsAssigned(assignmentId, TechnicianId.From(technician), recipe); repo.Update(request); await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CompleteAsync();
            Assert.True(await scope.ServiceProvider.GetRequiredService<IInstallationAssignmentReader>().IsAssignedInstallation(requestId.Value, propertyId, technician, client));
        }
        var nonDevice = await Snapshot(h, includeDevice: false); var beforeEvents = h.AssetMutationEvents;
        using (var response = await h.Send(HttpMethod.Put, path, Payload(technician), otherUser: true))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(8, dto.EnumerateObject().Count()); Assert.Equal(deviceId, dto.GetProperty("id").GetString());
            Assert.Equal((int)EDeviceStatus.Installed, dto.GetProperty("status").GetInt32()); Assert.DoesNotContain("key", dto.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }
        using (var verify = h.Scope())
        {
            var device = await verify.ServiceProvider.GetRequiredService<IIoTDeviceRepository>().FindByIdAsync(IoTDeviceId.From(deviceId)); Assert.NotNull(device);
            Assert.Equal(EDeviceStatus.Installed, device.Status); Assert.Equal(technician, device.InstalledByTechnicianId!.Value);
            Assert.Equal(at, device.InstalledAt); Assert.Equal("synthetic-after", device.FirmwareVersion);
            Assert.Equal(propertyId, device.AssignedPropertyId!.Value); Assert.Equal(requestId.Value, device.InstallationRequestId!.Value);
        }
        Assert.True(nonDevice == await Snapshot(h, includeDevice: false)); Assert.Equal(beforeEvents + 1, h.AssetMutationEvents);
        await Denied(path, Payload(technician), HttpStatusCode.Conflict); Assert.Equal(0, h.ForbiddenCalls);
        Console.WriteLine("PASS M3.13 real IAM/Profiles technicians, persisted request/assignment/property relations, authorized HTTP logical installation, exact legacy DTO and one Assets event");
        Console.WriteLine("PASS M3.13 unrelated active technician, spoofed technician, anonymous/Edge, incompatible/missing resource and reactivated request denied without mutations/events; persisted state from fresh scopes; repeat conflict; no external calls");
    }
    internal static async Task<string> Snapshot(RealBackendScenario h, bool includeDevice = true, bool includeProperties = true)
    {
        using var scope = h.Scope(); var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var devices = includeDevice ? JsonSerializer.Serialize(await ctx.IoTDevices.AsNoTracking().OrderBy(d => d.Id).ToListAsync()) : "";
        var data = new { devices, Properties = includeProperties ? await ctx.Set<Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates.Property>().AsNoTracking().OrderBy(p => p.Id).ToListAsync() : null,
            Requests = await ctx.ServiceRequests.AsNoTracking().OrderBy(r => r.RequestId).Select(r => new { r.RequestId, r.Status, r.PropertyId, r.SelectedTechnicianId, r.AssignmentId }).ToListAsync(),
            Assignments = await ctx.ServiceAssignments.AsNoTracking().OrderBy(a => a.AssignmentId).Select(a => new { a.AssignmentId, a.RequestId, a.TechnicianId, a.Status }).ToListAsync(),
            Outbox = await ctx.OutboxMessages.CountAsync(), Streams = await ctx.Set<Hampcoders.Electrolink.API.Processing.Domain.Model.Aggregates.DeviceReadingStream>().CountAsync() };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data))));
    }
}
