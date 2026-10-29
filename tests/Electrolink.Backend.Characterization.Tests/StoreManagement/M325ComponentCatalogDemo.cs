using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Interfaces.REST;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Commands;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

// M3.25: global component catalogue over real IAM/JWT/Profiles/Assets and EF/Npgsql; Admin writes, technicians read,
// and the technician then adds that catalogue entry to its own inventory (real stock mutation pending since M3.24).
public static class M325ComponentCatalogDemo
{
    public static async Task Run(string manifestPath)
    {
        var connection = await M319SubscriptionInstallationDemo.Connect(manifestPath);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath)); var m = json.RootElement;
        var task = m.GetProperty("taskId").GetString()!;
        using var h = new RealBackendScenario(connection, m.GetProperty("jwtSecret").GetString()!, m.GetProperty("edgeKey").GetString()!, "unused-synthetic-m325",
            machineScope: false, extraControllers: [typeof(ComponentsController), typeof(ComponentTypesController), typeof(TechnicianInventoriesController)]);
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + "aA1!";
        string Email(string who) => "m325-" + who + "-" + task + "@example.invalid";
        await h.RegisterAndComplete(Email("homeowner"), password, "00000001", other: true);
        await h.RegisterAndCompleteTechnician(Email("tech"), password, "00000006");
        string technician;
        using (var seed = h.Scope())
        {
            technician = (await seed.ServiceProvider.GetRequiredService<IProfilesContextFacade>().GetTechnicianIdByUserIdAsync(h.Owner.Id.Value))!;
            await seed.ServiceProvider.GetRequiredService<IStaffUserCommandService>().Handle(new CreateStaffUserCommand(Email("admin"), password, password, "Admin", "Synthetic", "Admin", null));
        }
        var root = "/api/v1/technicians/" + technician;
        async Task<string> Catalogue()
        {
            using var scope = h.Scope(); var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var types = await ctx.Set<ComponentType>().AsNoTracking().ToListAsync(); var parts = await ctx.Set<Component>().AsNoTracking().ToListAsync();
            return string.Join(",", types.Select(t => "T:" + t.Name + ":" + t.IsActive).Concat(parts.Select(c => "C:" + c.Name + ":" + c.IsActive)).OrderBy(v => v));
        }
        async Task Expect(HttpMethod method, string path, HttpStatusCode code, bool asOther = false, object? body = null, bool edge = false)
        {
            var before = await Catalogue();
            using var r = await h.Send(method, path, body, otherUser: asOther, anonymous: edge, edgeKey: edge);
            Assert.Equal(code, r.StatusCode);
            Assert.Equal(before, await Catalogue());
        }
        // Technician (default token) and HomeOwner (other token) cannot write; Edge is 401.
        await Expect(HttpMethod.Post, root + "/component-types", HttpStatusCode.Forbidden, body: new { name = "Denied", description = "x" });
        await Expect(HttpMethod.Post, root + "/components", HttpStatusCode.Forbidden, body: new { name = "Denied", description = "x", isActive = true });
        await Expect(HttpMethod.Post, root + "/component-types", HttpStatusCode.Forbidden, asOther: true, body: new { name = "Denied", description = "x" });
        await Expect(HttpMethod.Get, root + "/component-types", HttpStatusCode.Forbidden, asOther: true);
        await Expect(HttpMethod.Get, root + "/component-types", HttpStatusCode.Unauthorized, edge: true);
        // Admin writes the catalogue.
        await h.Login(Email("admin"), password, other: true);
        string typeId, componentId;
        using (var r = await h.Send(HttpMethod.Post, root + "/component-types", new { name = "Synthetic sensor " + task, description = "Synthetic" }, otherUser: true))
        { Assert.Equal(HttpStatusCode.OK, r.StatusCode); typeId = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("componentTypeId").GetString()!; }
        using (var r = await h.Send(HttpMethod.Post, root + "/components", new { name = "Synthetic module " + task, description = "Synthetic", isActive = true }, otherUser: true))
        { Assert.Equal(HttpStatusCode.OK, r.StatusCode); componentId = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("componentId").GetString()!; }
        await Expect(HttpMethod.Post, root + "/component-types", HttpStatusCode.Conflict, asOther: true, body: new { name = "Synthetic sensor " + task, description = "dup" });
        Assert.Contains("T:Synthetic sensor " + task + ":True", await Catalogue());
        // The technician reads the catalogue on its own route and stocks the entry in its own inventory.
        await Expect(HttpMethod.Get, root + "/component-types/" + typeId, HttpStatusCode.OK);
        await Expect(HttpMethod.Get, root + "/components", HttpStatusCode.OK);
        using (var r = await h.Send(HttpMethod.Post, root + "/inventory/stock-items", new { componentId, componentTypeId = typeId, quantity = 4, alertThreshold = 1 }))
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var r = await h.Send(HttpMethod.Patch, root + "/inventory/" + componentId + "/increase", new { amount = 2 })) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var scope = h.Scope())
        {
            var inventory = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<TechnicianInventory>().Include(i => i.StockItems).AsNoTracking().ToListAsync())
                .Single(i => i.TechnicianId.Value == technician);
            Assert.Equal(6, inventory.StockItems.Single(s => s.ComponentId.Value == componentId).QuantityAvailable);
        }
        // A type in use cannot be deleted, even by the Admin.
        await Expect(HttpMethod.Delete, root + "/component-types/" + typeId, HttpStatusCode.Conflict, asOther: true);
        Assert.Equal(0, h.ForbiddenCalls);
        Console.WriteLine("PASS M3.25 real catalogue: technician/HomeOwner writes 403, HomeOwner read 403, Edge 401; Admin creates (duplicate 409); technician reads own route and stocks 4+2=6 in own inventory; type in use delete 409");
    }
}
