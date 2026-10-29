using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Assets.Interfaces.REST;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

// M3.23: PropertyPortfoliosController over real IAM/JWT/Profiles/Assets and EF/Npgsql (portfolios created by ProfileCompleted).
public static class M323PortfolioOwnershipDemo
{
    public static async Task Run(string manifestPath)
    {
        var connection = await M319SubscriptionInstallationDemo.Connect(manifestPath);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath)); var m = json.RootElement;
        var task = m.GetProperty("taskId").GetString()!;
        using var h = new RealBackendScenario(connection, m.GetProperty("jwtSecret").GetString()!, m.GetProperty("edgeKey").GetString()!, "unused-synthetic-m323",
            machineScope: false, extraControllers: [typeof(PropertiesController), typeof(PropertyPortfoliosController)]);
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + "aA1!";
        await h.RegisterAndCompleteCompany("m323-company-" + task + "@example.invalid", password, "20000000004", other: true);
        await h.RegisterAndComplete("m323-owner-" + task + "@example.invalid", password, "00000001");
        string homeowner, company;
        using (var seed = h.Scope())
        {
            var profiles = seed.ServiceProvider.GetRequiredService<IProfilesContextFacade>();
            homeowner = (await profiles.GetProfileClaimsAsync(h.Owner.Id.Value))!.Value.RoleSubjectId!;
            company = (await profiles.GetProfileClaimsAsync(h.Other.Id.Value))!.Value.RoleSubjectId!;
        }
        var homeRoot = "/api/v1/homeowners/" + homeowner; var companyRoot = "/api/v1/companies/" + company;
        object NewProperty() => new
        {
            address = new { street = "Synthetic", number = "1", district = "Synthetic", city = "Synthetic", country = "Synthetic", postalCode = "00000" },
            geolocation = new { latitude = 0.1, longitude = 0d, accuracy = (int?)null, source = "MANUAL" }, propertyType = "Commercial"
        };
        string propertyId;
        using (var created = await h.Send(HttpMethod.Post, homeRoot + "/properties", NewProperty()))
        {
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            propertyId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("propertyId").GetString()!;
        }
        async Task<string> Entries(string owner, bool isCompany)
        {
            using var scope = h.Scope();
            var portfolio = await scope.ServiceProvider.GetRequiredService<IPropertyPortfolioRepository>()
                .FindByOwnerWithEntriesAsync(isCompany ? ClientIdentity.FromCompany(owner) : ClientIdentity.FromHomeowner(owner));
            Assert.NotNull(portfolio);
            return string.Join(",", portfolio.Entries.Select(e => e.PropertyId.Value).OrderBy(v => v));
        }
        async Task Expect(HttpMethod method, string path, HttpStatusCode code, bool other = false, object? body = null)
        {
            var before = (await Entries(homeowner, false), await Entries(company, true));
            using var r = await h.Send(method, path, body, otherUser: other);
            Assert.Equal(code, r.StatusCode);
            Assert.Equal(before, (await Entries(homeowner, false), await Entries(company, true)));
        }
        object Add(string id) => new { propertyId = id, nickname = "Synthetic " + task, isPrimary = true, occupancyStatus = "OwnerOccupied" };
        // Rejections before the owner acts.
        await Expect(HttpMethod.Get, homeRoot + "/property-portfolios", HttpStatusCode.NotFound, other: true);
        await Expect(HttpMethod.Post, homeRoot + "/property-portfolios/properties", HttpStatusCode.NotFound, other: true, body: Add(propertyId));
        await Expect(HttpMethod.Post, companyRoot + "/property-portfolios/properties", HttpStatusCode.NotFound, other: true, body: Add(propertyId));
        await Expect(HttpMethod.Get, "/api/v1/companies/" + homeowner + "/property-portfolios", HttpStatusCode.NotFound);
        using (var edge = await h.Send(HttpMethod.Get, homeRoot + "/property-portfolios", anonymous: true, edgeKey: true)) Assert.Equal(HttpStatusCode.Unauthorized, edge.StatusCode);
        // Owner adds; the Company cannot remove it; the owner can.
        using (var added = await h.Send(HttpMethod.Post, homeRoot + "/property-portfolios/properties", Add(propertyId))) Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.Equal(propertyId, await Entries(homeowner, false));
        await Expect(HttpMethod.Delete, homeRoot + "/property-portfolios/properties/" + propertyId + "?reason=synthetic", HttpStatusCode.NotFound, other: true);
        await Expect(HttpMethod.Get, companyRoot + "/property-portfolios", HttpStatusCode.OK, other: true);
        using (var removed = await h.Send(HttpMethod.Delete, homeRoot + "/property-portfolios/properties/" + propertyId + "?reason=synthetic")) Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal("", await Entries(homeowner, false));
        Assert.Equal(0, h.ForbiddenCalls);
        Console.WriteLine("PASS M3.23 real portfolios: owner adds/removes own property; other owner, foreign property, crossed route 404 and Edge 401 without entry changes");
    }
}
