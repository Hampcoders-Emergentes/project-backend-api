using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Hampcoders.Electrolink.API.Monitoring.Interfaces.ACL;
using Hampcoders.Electrolink.API.Planning.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Planning.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Infrastructure.Persistence.EFC.Repositories;
using Hampcoders.Electrolink.API.Planning.Interfaces.REST;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.ACL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

// M3.26: ServiceCatalogController with real IAM/JWT/Profiles, real catalog command/query services and EF/Npgsql.
// Doubles: Monitoring facade reports 0 active/in-progress services; the Subscriptions facade is blocked.
public static class M326ServiceCatalogDemo
{
    public static async Task Run(string manifestPath)
    {
        // Same process-wide Npgsql setting as Program.cs (catalog jsonb lists); this mode runs in its own process.
#pragma warning disable CS0618
        NpgsqlConnection.GlobalTypeMapper.EnableDynamicJson();
#pragma warning restore CS0618
        var connection = await M319SubscriptionInstallationDemo.Connect(manifestPath);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath)); var m = json.RootElement;
        var task = m.GetProperty("taskId").GetString()!;
        var blocked = 0;
        using var h = new RealBackendScenario(connection, m.GetProperty("jwtSecret").GetString()!, m.GetProperty("edgeKey").GetString()!, "unused-synthetic-m326",
            machineScope: false, extraControllers: [typeof(ServiceCatalogController)], extraServices: services =>
            {
                services.AddScoped<IServiceCatalogRepository, ServiceCatalogRepository>();
                services.AddScoped<ExternalProfilesService>(); services.AddScoped<ExternalAssetsService>();
                services.AddScoped<ExternalMonitoringService>(); services.AddScoped<ExternalSubscriptionsService>();
                services.AddSingleton(Assets.AssignmentHttpFixture.Proxy<IMonitoringContextFacade>((mi, a) =>
                    mi.Name is nameof(IMonitoringContextFacade.CountActiveServicesForRecipeAsync) or nameof(IMonitoringContextFacade.CountInProgressServicesForRecipeAsync)
                        ? Task.FromResult(0) : throw new InvalidOperationException("Blocked monitoring call " + mi.Name)));
                services.AddSingleton(Assets.AssignmentHttpFixture.Proxy<ISubscriptionContextFacade>((mi, a) => { blocked++; throw new InvalidOperationException("Blocked subscription call"); }));
                services.AddScoped<IServiceCatalogCommandService, ServiceCatalogCommandService>();
                services.AddScoped<IServiceDesignQueryService, ServiceDesignQueryService>();
            });
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + "aA1!";
        await h.RegisterAndCompleteTechnician("m326-other-" + task + "@example.invalid", password, "00000007", other: true);
        await h.RegisterAndCompleteTechnician("m326-owner-" + task + "@example.invalid", password, "00000008");
        string owner;
        using (var seed = h.Scope()) owner = (await seed.ServiceProvider.GetRequiredService<IProfilesContextFacade>().GetTechnicianIdByUserIdAsync(h.Owner.Id.Value))!;
        async Task<string> Rows()
        {
            using var scope = h.Scope();
            var catalogs = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<ServiceCatalog>().Include(c => c.Recipes).AsNoTracking().ToListAsync();
            return string.Join(",", catalogs.Select(c => c.TechnicianId.Value + ":" + string.Join("|", c.Recipes.Select(r => r.ServiceName + "=" + r.IsActive))).OrderBy(v => v));
        }
        async Task Expect(HttpMethod method, string path, HttpStatusCode code, bool asOther = false, object? body = null, bool edge = false, bool unchanged = true)
        {
            var before = await Rows();
            using var r = await h.Send(method, path, body, otherUser: asOther, anonymous: edge, edgeKey: edge);
            Assert.Equal(code, r.StatusCode);
            if (unchanged) Assert.Equal(before, await Rows());
        }
        await Expect(HttpMethod.Post, "/api/v1/technicians/me/catalog", HttpStatusCode.Created, unchanged: false);
        string recipeId;
        using (var r = await h.Send(HttpMethod.Post, "/api/v1/technicians/me/catalog/recipes", new
        {
            serviceName = "Synthetic installation " + task, serviceDescription = "Synthetic", serviceCategory = "Installation", requiresIoTCertification = false,
            componentRequirements = Array.Empty<object>(), estimatedDurationHours = 0, estimatedDurationMinutes = 30,
            pricing = new { materialsEstimate = 0m, laborCost = 1m, totalPrice = 1m, currency = "PEN" },
            prerequisites = Array.Empty<string>(), deliverables = Array.Empty<string>(), warrantyMonths = 0
        }))
        { Assert.Equal(HttpStatusCode.Created, r.StatusCode); recipeId = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recipeId").GetString()!; }
        var root = "/api/v1/technicians/" + owner + "/catalog";
        await Expect(HttpMethod.Get, root, HttpStatusCode.OK);
        await Expect(HttpMethod.Get, "/api/v1/technicians/me/catalog/recipes/" + recipeId, HttpStatusCode.OK);
        // The other technician cannot read or change the owner's catalog.
        await Expect(HttpMethod.Get, root, HttpStatusCode.NotFound, asOther: true);
        await Expect(HttpMethod.Patch, root + "/recipes/" + recipeId, HttpStatusCode.NotFound, asOther: true, body: new { serviceName = "Hijacked" });
        await Expect(HttpMethod.Delete, root + "/recipes/" + recipeId, HttpStatusCode.NotFound, asOther: true, body: new { reason = "Seasonal" });
        await Expect(HttpMethod.Get, root, HttpStatusCode.Unauthorized, edge: true);
        // The owner deactivates and reactivates through "me" (previously TechnicianId.From("me") failed).
        await Expect(HttpMethod.Delete, "/api/v1/technicians/me/catalog/recipes/" + recipeId, HttpStatusCode.NoContent, body: new { reason = "Seasonal" }, unchanged: false);
        Assert.Contains("=False", await Rows());
        await Expect(HttpMethod.Post, "/api/v1/technicians/me/catalog/recipes/" + recipeId + "/reactivate", HttpStatusCode.NoContent, unchanged: false);
        Assert.Contains("Synthetic installation " + task + "=True", await Rows());
        Assert.Equal(0, blocked); Assert.Equal(0, h.ForbiddenCalls);
        Console.WriteLine("PASS M3.26 real catalog: owner creates catalog/recipe via me, reads, deactivates and reactivates; other technician 404 on read/update/deactivate; Edge 401; rows unchanged on rejection");
    }
}
