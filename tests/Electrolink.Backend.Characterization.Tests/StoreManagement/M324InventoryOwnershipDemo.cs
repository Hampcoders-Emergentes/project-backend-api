using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Hampcoders.Electrolink.API.Assets.Interfaces.REST;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

// M3.24: TechnicianInventoriesController over real IAM/JWT/Profiles/Assets and EF/Npgsql (inventories created by ProfileCompleted).
public static class M324InventoryOwnershipDemo
{
    public static async Task Run(string manifestPath)
    {
        var connection = await M319SubscriptionInstallationDemo.Connect(manifestPath);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath)); var m = json.RootElement;
        var task = m.GetProperty("taskId").GetString()!;
        using var h = new RealBackendScenario(connection, m.GetProperty("jwtSecret").GetString()!, m.GetProperty("edgeKey").GetString()!, "unused-synthetic-m324",
            machineScope: false, extraControllers: [typeof(TechnicianInventoriesController)]);
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + "aA1!";
        await h.RegisterAndCompleteTechnician("m324-other-" + task + "@example.invalid", password, "00000004", other: true);
        await h.RegisterAndCompleteTechnician("m324-owner-" + task + "@example.invalid", password, "00000005");
        string owner, other;
        using (var seed = h.Scope())
        {
            var profiles = seed.ServiceProvider.GetRequiredService<IProfilesContextFacade>();
            owner = (await profiles.GetTechnicianIdByUserIdAsync(h.Owner.Id.Value))!; other = (await profiles.GetTechnicianIdByUserIdAsync(h.Other.Id.Value))!;
        }
        async Task<string> Rows()
        {
            using var scope = h.Scope(); var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var inventories = await ctx.Set<Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates.TechnicianInventory>().AsNoTracking().ToListAsync();
            return string.Join(",", inventories.Select(i => i.TechnicianId.Value + ":" + i.StockItems.Count).OrderBy(v => v));
        }
        var before = await Rows();
        Assert.Contains(owner, before); Assert.Contains(other, before);
        async Task Expect(HttpMethod method, string path, HttpStatusCode code, bool asOther = false, object? body = null, bool edge = false)
        {
            using var r = await h.Send(method, path, body, otherUser: asOther, anonymous: edge, edgeKey: edge);
            Assert.Equal(code, r.StatusCode);
            Assert.Equal(before, await Rows());
        }
        var own = "/api/v1/technicians/" + owner + "/inventory";
        await Expect(HttpMethod.Get, own, HttpStatusCode.OK);
        await Expect(HttpMethod.Get, own + "/stock-items", HttpStatusCode.OK);
        await Expect(HttpMethod.Post, own, HttpStatusCode.Conflict);                                     // already created by ProfileCompleted
        await Expect(HttpMethod.Get, own, HttpStatusCode.NotFound, asOther: true);
        await Expect(HttpMethod.Post, own, HttpStatusCode.NotFound, asOther: true);
        await Expect(HttpMethod.Patch, own + "/cmpt-synthetic-m324/increase", HttpStatusCode.NotFound, asOther: true, body: new { amount = 1 });
        await Expect(HttpMethod.Delete, own + "/cmpt-synthetic-m324", HttpStatusCode.NotFound, asOther: true);
        await Expect(HttpMethod.Get, own, HttpStatusCode.Unauthorized, edge: true);
        Assert.Equal(0, h.ForbiddenCalls);
        Console.WriteLine("PASS M3.24 real technicians: own inventory 200 (create again 409); another technician 404 on read/create/increase/remove; Edge 401; inventory rows unchanged");
    }
}
