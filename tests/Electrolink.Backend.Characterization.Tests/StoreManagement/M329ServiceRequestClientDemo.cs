using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Hampcoders.Electrolink.API.Planning.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Planning.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Infrastructure.Persistence.EFC.Repositories;
using Hampcoders.Electrolink.API.Planning.Interfaces.REST;
using Hampcoders.Electrolink.API.Profiles.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.ACL;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

// M3.29: ServiceRequestController with real IAM/JWT/Profiles, the real request command/query services and EF/Npgsql.
// Doubles: Subscriptions eligibility (synthetic BASIC plan, can create); matching and context snapshot are blocked.
public static class M329ServiceRequestClientDemo
{
    public static async Task Run(string manifestPath)
    {
        var connection = await M319SubscriptionInstallationDemo.Connect(manifestPath);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath)); var m = json.RootElement;
        var task = m.GetProperty("taskId").GetString()!;
        var blocked = 0;
        T Block<T>() where T : class => Assets.AssignmentHttpFixture.Proxy<T>((mi, a) => { blocked++; throw new InvalidOperationException("Blocked " + mi.Name); });
        using var h = new RealBackendScenario(connection, m.GetProperty("jwtSecret").GetString()!, m.GetProperty("edgeKey").GetString()!, "unused-synthetic-m329",
            machineScope: false, extraControllers: [typeof(ServiceRequestController)], extraServices: services =>
            {
                services.AddScoped<IServiceCatalogRepository, ServiceCatalogRepository>();
                services.AddScoped<ExternalProfilesService>(); services.AddScoped<ExternalAssetsService>(); services.AddScoped<ExternalSubscriptionsService>();
                services.AddSingleton(Assets.AssignmentHttpFixture.Proxy<ISubscriptionContextFacade>((mi, a) => mi.Name == nameof(ISubscriptionContextFacade.GetRequestEligibilityAsync)
                    ? Task.FromResult<(bool canCreate, string planType, int? remainingRequests, bool canMarkAsPriority)>((true, "BASIC", 3, false))
                    : throw new InvalidOperationException("Blocked subscription call " + mi.Name)));
                services.AddSingleton(Block<IServiceAssignmentCommandService>()); services.AddSingleton(Block<IContextSnapshotService>());
                services.AddScoped<IServiceRequestCommandService, ServiceRequestCommandService>();
                services.AddScoped<IServiceDesignQueryService, ServiceDesignQueryService>();
            });
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + "aA1!";
        await h.RegisterAndComplete("m329-other-" + task + "@example.invalid", password, "00000002", other: true);
        await h.RegisterAndComplete("m329-owner-" + task + "@example.invalid", password, "00000001");
        const string root = "/api/v1/service-requests";
        string requestId;
        using (var r = await h.Send(HttpMethod.Post, root))
        { Assert.Equal(HttpStatusCode.Created, r.StatusCode); requestId = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestId").GetString()!; }
        using (var r = await h.Send(HttpMethod.Get, root + "/" + requestId)) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        // Another HomeOwner: generic 404 on read, cancel and available services; the request is unchanged.
        foreach (var (method, path, body) in new (HttpMethod, string, object?)[] { (HttpMethod.Get, root + "/" + requestId, null),
            (HttpMethod.Delete, root + "/" + requestId, new { reason = "CHANGED_MIND" }), (HttpMethod.Get, root + "/" + requestId + "/available-services", null) })
        {
            using var r = await h.Send(method, path, body, otherUser: true);
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        }
        using (var scope = h.Scope())
            Assert.Equal("Draft", (await scope.ServiceProvider.GetRequiredService<IServiceRequestRepository>().FindByIdAsync(Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects.RequestId.From(requestId)))!.Status.ToString());
        // The owner's profile is suspended (as PaymentFailedEventHandler does); its still-valid JWT keeps the HomeOwner claims.
        using (var scope = h.Scope())
        {
            var profile = await scope.ServiceProvider.GetRequiredService<IProfileRepository>().FindByUserIdAsync(UserId.From(h.Owner.Id.Value));
            profile!.Suspend(); profile.ClearDomainEvents();
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CompleteAsync();
        }
        foreach (var (method, path, body) in new (HttpMethod, string, object?)[] { (HttpMethod.Get, root + "/" + requestId, null), (HttpMethod.Post, root, null),
            (HttpMethod.Get, root + "/eligibility", null), (HttpMethod.Delete, root + "/" + requestId, new { reason = "CHANGED_MIND" }) })
        {
            using var r = await h.Send(method, path, body);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        using (var edge = await h.Send(HttpMethod.Get, root + "/eligibility", anonymous: true, edgeKey: true)) Assert.Equal(HttpStatusCode.Unauthorized, edge.StatusCode);
        Assert.Equal(0, blocked); Assert.Equal(0, h.ForbiddenCalls);
        Console.WriteLine("PASS M3.29 real requests: owner initiates and reads; other HomeOwner 404 on read/cancel/available-services; after profile suspension the old token's client claims get 403; Edge 401");
    }
}
