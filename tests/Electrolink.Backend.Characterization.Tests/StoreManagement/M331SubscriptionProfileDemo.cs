using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Hampcoders.Electrolink.API.Profiles.Domain.Repositories;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Repository;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Services;
using Hampcoders.Electrolink.API.Subscriptions.Infrastructure.Persistence.EFC.Repositories;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.ACL;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.REST;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

// M3.31: SubscriptionsController reads over real IAM/JWT/Profiles, the real subscription query service and EF/Npgsql.
// Subscriptions are seeded with the real repository; the command service (payment provider) is blocked and not called.
public static class M331SubscriptionProfileDemo
{
    public static async Task Run(string manifestPath)
    {
        var connection = await M319SubscriptionInstallationDemo.Connect(manifestPath);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath)); var m = json.RootElement;
        var task = m.GetProperty("taskId").GetString()!;
        var blocked = 0;
        using var h = new RealBackendScenario(connection, m.GetProperty("jwtSecret").GetString()!, m.GetProperty("edgeKey").GetString()!, "unused-synthetic-m331",
            machineScope: false, extraControllers: [typeof(SubscriptionsController)], extraServices: services =>
            {
                services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
                services.AddScoped<ISubscriptionQueryService, SubscriptionQueryService>();
                services.AddScoped<ISubscriptionProfileResolver, SubscriptionProfileResolver>();
                services.AddSingleton(Assets.AssignmentHttpFixture.Proxy<ISubscriptionCommandService>((mi, a) => { blocked++; throw new InvalidOperationException("Blocked payment command"); }));
            });
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + "aA1!";
        await h.RegisterAndComplete("m331-other-" + task + "@example.invalid", password, "00000002", other: true);
        await h.RegisterAndComplete("m331-owner-" + task + "@example.invalid", password, "00000001");
        string ownerSubscription, otherSubscription;
        using (var seed = h.Scope())
        {
            var sp = seed.ServiceProvider; var profiles = sp.GetRequiredService<IProfilesContextFacade>();
            async Task<string> Seed(string userId)
            {
                var claims = (await profiles.GetProfileClaimsAsync(userId))!.Value;
                var subscription = Subscription.Initialize(UserId.From(userId), ProfileId.From(claims.ProfileId), BusinessRole.Homeowner,
                    StripeCustomerId.From("cus_synthetic_" + Guid.NewGuid().ToString("N")), new HomeownerBillingPolicy());
                subscription.ClearDomainEvents();
                await sp.GetRequiredService<ISubscriptionRepository>().AddAsync(subscription);
                return subscription.SubscriptionId.Value;
            }
            ownerSubscription = await Seed(h.Owner.Id.Value); otherSubscription = await Seed(h.Other.Id.Value);
            await sp.GetRequiredService<IUnitOfWork>().CompleteAsync();
        }
        async Task<string> MySubscription(bool other)
        {
            using var r = await h.Send(HttpMethod.Get, "/api/v1/subscriptions/me", otherUser: other);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("subscriptionId").GetString()!;
        }
        Assert.Equal(ownerSubscription, await MySubscription(false));
        Assert.Equal(otherSubscription, await MySubscription(true));
        async Task ChangeProfile(string userId, Action<Hampcoders.Electrolink.API.Profiles.Domain.Model.Aggregates.Profile> change)
        {
            using var scope = h.Scope();
            var profile = await scope.ServiceProvider.GetRequiredService<IProfileRepository>().FindByUserIdAsync(UserId.From(userId));
            change(profile!); profile!.ClearDomainEvents(); await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CompleteAsync();
        }
        // A failed payment suspends the profile: its owner can still read status and eligibility to regularize.
        await ChangeProfile(h.Owner.Id.Value, p => p.Suspend());
        Assert.Equal(ownerSubscription, await MySubscription(false));
        foreach (var path in new[] { "me/status-alert", "me/eligibility", "me/payment-history" })
        {
            using var r = await h.Send(HttpMethod.Get, "/api/v1/subscriptions/" + path);
            Assert.True(r.IsSuccessStatusCode, path + " returned " + (int)r.StatusCode);
        }
        // A deactivated profile is rejected even with a still-valid token.
        await ChangeProfile(h.Other.Id.Value, p => p.Deactivate());
        using (var r = await h.Send(HttpMethod.Get, "/api/v1/subscriptions/me", otherUser: true)) Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        using (var edge = await h.Send(HttpMethod.Get, "/api/v1/subscriptions/me", anonymous: true, edgeKey: true)) Assert.Equal(HttpStatusCode.Unauthorized, edge.StatusCode);
        Assert.Equal(0, blocked); Assert.Equal(0, h.ForbiddenCalls);
        Console.WriteLine("PASS M3.31 real subscriptions: each owner reads its own subscription; payment-suspended profile still reads status/eligibility/history; deactivated profile 403; Edge 401");
    }
}
