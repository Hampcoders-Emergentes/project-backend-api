using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Interfaces.REST;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

// M3.22: PropertiesController over real IAM/JWT/Profiles/Assets and EF/Npgsql; route owner bound to the actor.
public static class M322PropertyOwnershipDemo
{
    public static async Task Run(string manifestPath)
    {
        var connection = await M319SubscriptionInstallationDemo.Connect(manifestPath);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath)); var m = json.RootElement;
        var task = m.GetProperty("taskId").GetString()!;
        using var h = new RealBackendScenario(connection, m.GetProperty("jwtSecret").GetString()!, m.GetProperty("edgeKey").GetString()!, "unused-synthetic-m322",
            machineScope: false, extraControllers: [typeof(PropertiesController)]);
        using (var empty = h.Scope())
            Assert.Equal(0, await empty.ServiceProvider.GetRequiredService<AppDbContext>().Set<Property>().CountAsync());
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + "aA1!";
        await h.RegisterAndCompleteCompany("m322-company-" + task + "@example.invalid", password, "20000000003", other: true);
        await h.RegisterAndComplete("m322-owner-" + task + "@example.invalid", password, "00000001");
        string homeowner, company;
        using (var seed = h.Scope())
        {
            var profiles = seed.ServiceProvider.GetRequiredService<IProfilesContextFacade>();
            homeowner = (await profiles.GetProfileClaimsAsync(h.Owner.Id.Value))!.Value.RoleSubjectId!;
            company = (await profiles.GetProfileClaimsAsync(h.Other.Id.Value))!.Value.RoleSubjectId!;
        }
        var own = "/api/v1/homeowners/" + homeowner + "/properties";
        var companyRoot = "/api/v1/companies/" + company + "/properties";
        object Body(double lat) => new
        {
            address = new { street = "Synthetic", number = "1", district = "Synthetic", city = "Synthetic", country = "Synthetic", postalCode = "00000" },
            geolocation = new { latitude = lat, longitude = 0d, accuracy = (int?)null, source = "MANUAL" }, propertyType = "Commercial"
        };
        async Task<string> CreateAs(string root, bool other, double lat)
        {
            using var r = await h.Send(HttpMethod.Post, root, Body(lat), otherUser: other); Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("propertyId").GetString()!;
        }
        var homeProperty = await CreateAs(own, false, 0.1);
        var companyProperty = await CreateAs(companyRoot, true, 0.2);
        async Task<string> Row(string id)
        {
            using var scope = h.Scope();
            var p = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Property>().AsNoTracking().SingleAsync(x => x.Id == Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects.PropertyId.From(id));
            return JsonSerializer.Serialize(new { p.IsActive, Status = p.Status.ToString(), p.Geolocation.Latitude, Owner = p.Owner.ToString() });
        }
        async Task Expect(HttpMethod method, string path, HttpStatusCode code, bool other = false, object? body = null)
        {
            var before = (await Row(homeProperty), await Row(companyProperty));
            using var r = await h.Send(method, path, body, otherUser: other);
            Assert.Equal(code, r.StatusCode);
            if (code != HttpStatusCode.OK) Assert.Equal(before, (await Row(homeProperty), await Row(companyProperty)));
        }
        // Owners on their own routes.
        await Expect(HttpMethod.Get, own + "/" + homeProperty, HttpStatusCode.OK);
        await Expect(HttpMethod.Get, companyRoot + "/" + companyProperty, HttpStatusCode.OK, other: true);
        // The Company names the homeowner's route, or reaches the homeowner's property through its own route.
        await Expect(HttpMethod.Get, own, HttpStatusCode.NotFound, other: true);
        await Expect(HttpMethod.Post, own, HttpStatusCode.NotFound, other: true, body: Body(0.3));
        await Expect(HttpMethod.Get, companyRoot + "/" + homeProperty, HttpStatusCode.NotFound, other: true);
        await Expect(HttpMethod.Patch, companyRoot + "/" + homeProperty + "/deactivate", HttpStatusCode.NotFound, other: true);
        await Expect(HttpMethod.Delete, companyRoot + "/" + homeProperty + "?reason=synthetic", HttpStatusCode.NotFound, other: true);
        await Expect(HttpMethod.Patch, companyRoot + "/" + homeProperty + "/location", HttpStatusCode.NotFound, other: true,
            body: new { geolocation = new { latitude = 9d, longitude = 9d, accuracy = (int?)null, source = "MANUAL" } });
        // Route prefix that does not match the actor's role.
        await Expect(HttpMethod.Get, "/api/v1/companies/" + homeowner + "/properties/" + homeProperty, HttpStatusCode.NotFound);
        // Anonymous and Edge-only.
        using (var anonymous = await h.Send(HttpMethod.Get, own, anonymous: true)) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using (var edge = await h.Send(HttpMethod.Get, own, anonymous: true, edgeKey: true)) Assert.Equal(HttpStatusCode.Unauthorized, edge.StatusCode);
        Console.WriteLine("PASS M3.22 owners read their own properties; other owner/foreign property/crossed route 404 and anonymous/Edge 401 with rows unchanged");
        // The owner's own mutation persists (owner-filtered lookup over real SQL).
        await Expect(HttpMethod.Patch, own + "/" + homeProperty + "/deactivate", HttpStatusCode.OK);
        Assert.Contains("\"IsActive\":false", await Row(homeProperty));
        Assert.Equal(0, h.ForbiddenCalls);
        Console.WriteLine("PASS M3.22 owner deactivation persisted through the owner-filtered query; no external calls");
    }
}
