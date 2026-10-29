using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Electrolink.Backend.Characterization.Tests.Assets;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Components;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Services;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.ACL;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.REST;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using SubscriptionBusinessRole = Hampcoders.Electrolink.API.Subscriptions.Domain.Model.ValueObjects.BusinessRole;
namespace Electrolink.Backend.Characterization.Tests.Subscriptions;

// M3.31 (user decision): subscription endpoints use the ProfileId resolved server-side from the IAM user, never the JWT claim,
// and accept Active and Suspended profiles (a failed payment suspends the profile and the owner must be able to pay).
public sealed class SubscriptionProfileAccessTests
{
    static readonly string[] Actions = ["me", "eligibility", "payment-history", "status-alert", "checkout", "cancel", "portal"];

    [Theory]
    [InlineData("victim", "prof-victim")]
    [InlineData("payment-suspended", "prof-payment-suspended")]
    [InlineData("technician", "prof-technician")]
    public async Task Active_or_payment_suspended_profile_uses_its_own_profile_id_not_the_jwt_claim(string token, string profileId)
    {
        using var h = new SubscriptionFixture();
        foreach (var action in Actions)
        {
            using var r = await h.Act(token, action);
            Assert.True(r.IsSuccessStatusCode, action + " returned " + (int)r.StatusCode);
        }
        Assert.Equal(Actions.Length, h.ProfileIds.Count);
        Assert.All(h.ProfileIds, p => Assert.Equal(profileId, p));
    }

    [Theory]
    [InlineData("inactive-profile")]
    [InlineData("no-profile")]
    [InlineData("admin")]
    public async Task Deactivated_or_missing_profile_is_403_without_reaching_the_services(string token)
    {
        using var h = new SubscriptionFixture();
        foreach (var action in Actions)
        {
            using var r = await h.Act(token, action);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        Assert.Empty(h.ProfileIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("edge")]
    public async Task Missing_or_invalid_token_and_edge_key_are_401(string? token)
    {
        using var h = new SubscriptionFixture();
        using var r = await h.Act(token, "me");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Empty(h.ProfileIds);
    }
}

internal sealed class SubscriptionFixture : IDisposable
{
    internal List<string> ProfileIds { get; } = [];
    private readonly TestServer server;
    private readonly HttpClient client;

    static string ProfileOf(object message) => (string)message.GetType().GetProperty("ProfileId")!.GetValue(message)!;

    internal SubscriptionFixture()
    {
        var subscription = Subscription.Initialize(UserId.From("us-synthetic-m331"), ProfileId.From("prof-synthetic-m331"),
            SubscriptionBusinessRole.Homeowner, StripeCustomerId.From("cus_synthetic"), new HomeownerBillingPolicy());
        var queries = AssignmentHttpFixture.Proxy<ISubscriptionQueryService>((m, a) =>
        {
            ProfileIds.Add(ProfileOf(a![0]!));
            return a[0] switch
            {
                GetMySubscriptionQuery => Task.FromResult(subscription),
                GetRequestEligibilityQuery => Task.FromResult(new RequestEligibilityResult(true, false, 3, "BASIC", false)),
                GetPaymentHistoryQuery => Task.FromResult<IEnumerable<PaymentRecord>>([]),
                GetSubscriptionStatusAlertQuery => Task.FromResult<Subscription?>(null),
                _ => throw new InvalidOperationException("Unexpected query")
            };
        });
        // Command double: records the profile; no payment provider is contacted.
        var commands = AssignmentHttpFixture.Proxy<ISubscriptionCommandService>((m, a) =>
        {
            ProfileIds.Add(ProfileOf(a![0]!));
            return a[0] switch
            {
                InitiateCheckoutCommand => Task.FromResult(new InitiateCheckoutResult("https://checkout.invalid/session", "cs_synthetic")),
                CancelSubscriptionCommand => Task.FromResult(subscription),
                OpenCustomerPortalCommand => Task.FromResult(new CustomerPortalResult("https://portal.invalid/session")),
                _ => throw new InvalidOperationException("Unexpected command")
            };
        });
        var actors = new OwnerActorDoubles();
        server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            actors.AddTo(services);
            services.AddSingleton<ISubscriptionProfileResolver>(sp => new SubscriptionProfileResolver(sp.GetRequiredService<IProfilesContextFacade>()));
            services.AddSingleton(queries); services.AddSingleton(commands);
            // The controller keeps the framework [Authorize]; this scheme stands in for JWT bearer and exposes a forged profileId claim.
            services.AddAuthentication("Synthetic").AddScheme<AuthenticationSchemeOptions, ForgedClaimsHandler>("Synthetic", _ => { });
            services.AddAuthorization();
            services.AddControllers(o => o.Conventions.Add(new Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Configuration.KebabCaseRouteNamingConvention()))
                .AddApplicationPart(typeof(SubscriptionsController).Assembly).ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlySubscriptions()));
        }).Configure(app =>
        {
            app.UseRouting(); app.UseMiddleware<RequestAuthorizationMiddleware>(); app.UseAuthentication(); app.UseAuthorization();
            app.UseEndpoints(e => e.MapControllers());
        })); client = server.CreateClient();
    }

    internal Task<HttpResponseMessage> Act(string? token, string action) => action switch
    {
        "me" => Send(HttpMethod.Get, "me", token),
        "eligibility" => Send(HttpMethod.Get, "me/eligibility", token),
        "payment-history" => Send(HttpMethod.Get, "me/payment-history", token),
        "status-alert" => Send(HttpMethod.Get, "me/status-alert", token),
        "checkout" => Send(HttpMethod.Post, "checkout", token, new { planType = "PREMIUM", billingCycle = "MONTHLY", successUrl = "https://app.invalid/ok", cancelUrl = "https://app.invalid/cancel" }),
        "cancel" => Send(HttpMethod.Post, "me/cancel", token, new { reason = "synthetic", feedback = (string?)null }),
        "portal" => Send(HttpMethod.Post, "me/portal", token, new { returnUrl = "https://app.invalid/back" }),
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, "/api/v1/subscriptions/" + path);
        if (token == "edge") request.Headers.Add("X-Api-Key", "synthetic-edge-only");
        else if (token != null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        if (body != null) request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    public void Dispose() { client.Dispose(); server.Dispose(); }

    private sealed class ForgedClaimsHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.Ordinal)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim("profileId", "prof-forged-m331"), new Claim("businessRole", "HomeOwner")], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class OnlySubscriptions : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        { foreach (var c in feature.Controllers.ToArray()) if (c.AsType() != typeof(SubscriptionsController)) feature.Controllers.Remove(c); }
    }
}
