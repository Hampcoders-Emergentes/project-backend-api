using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Electrolink.Backend.Characterization.Tests.Assets;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Components;
using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Interfaces.REST;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.Planning;

// M3.29: ServiceRequestController resolves the client server-side (active, concordant HomeOwner/Company profile of the
// IAM user) instead of trusting JWT profile claims, and checks the request belongs to that client before acting.
public sealed class ServiceRequestClientTests
{
    static readonly string[] OnRequest = ["property", "available-services", "recipe", "details", "confirm", "cancel", "summary"];

    [Fact]
    public async Task Active_client_acts_with_the_server_resolved_identity_even_if_claims_say_otherwise()
    {
        using var h = new ServiceRequestClientFixture(forgedClaims: true);
        foreach (var action in OnRequest.Prepend("initiate").Prepend("eligibility"))
        {
            using var r = await h.Act("victim", action, h.OwnRequest);
            Assert.True(r.IsSuccessStatusCode, action + " returned " + (int)r.StatusCode);
        }
        Assert.Equal(6, h.Commands.Count);
        Assert.All(h.Commands, c => Assert.Equal(ClientIdentity.FromHomeowner(OwnerActorDoubles.Victim), ServiceRequestClientFixture.ClientOf(c)));
        Assert.All(h.QueriedClients, c => Assert.Equal(ClientIdentity.FromHomeowner(OwnerActorDoubles.Victim), c));
    }

    [Fact]
    public async Task Another_clients_or_a_missing_request_is_a_generic_404_without_commands()
    {
        using var h = new ServiceRequestClientFixture();
        foreach (var request in new[] { h.ForeignRequest, "req-missing-m329", "not-a-request" })
            foreach (var action in OnRequest)
            {
                using var r = await h.Act("victim", action, request);
                Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
                Assert.Equal("{\"message\":\"Resource not found.\"}", await r.Content.ReadAsStringAsync());
            }
        Assert.Empty(h.Commands); Assert.Equal(0, h.AvailableServiceQueries);
    }

    [Theory]
    [InlineData("inactive-profile")]
    [InlineData("subject-mismatch")]
    [InlineData("no-profile")]
    [InlineData("technician")]
    [InlineData("admin")]
    public async Task Actor_without_an_active_client_profile_is_403_even_with_client_claims(string token)
    {
        using var h = new ServiceRequestClientFixture(forgedClaims: true);
        foreach (var action in OnRequest.Prepend("initiate").Prepend("eligibility"))
        {
            using var r = await h.Act(token, action, h.OwnRequest);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        Assert.Empty(h.Commands); Assert.Equal(0, h.AvailableServiceQueries);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("edge")]
    public async Task Missing_or_invalid_token_and_edge_key_are_401(string? token)
    {
        using var h = new ServiceRequestClientFixture();
        using var r = await h.Act(token, "initiate", h.OwnRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Empty(h.Commands);
    }
}

internal sealed class ServiceRequestClientFixture : IDisposable
{
    internal List<object> Commands { get; } = [];
    internal List<ClientIdentity> QueriedClients { get; } = [];
    internal int AvailableServiceQueries { get; private set; }
    private readonly ServiceRequest own = ServiceRequest.Initiate(ClientIdentity.FromHomeowner(OwnerActorDoubles.Victim), false, null);
    private readonly ServiceRequest foreign = ServiceRequest.Initiate(ClientIdentity.FromHomeowner(OwnerActorDoubles.Attacker), false, null);
    internal string OwnRequest => own.RequestId.Value;
    internal string ForeignRequest => foreign.RequestId.Value;
    private readonly TestServer server;
    private readonly HttpClient client;

    internal static ClientIdentity ClientOf(object command) => (ClientIdentity)command.GetType().GetProperty("Client")!.GetValue(command)!;

    internal ServiceRequestClientFixture(bool forgedClaims = false)
    {
        var requests = new[] { own, foreign };
        // Command service double: records what reaches it (real handlers are unchanged by this cut).
        var commands = AssignmentHttpFixture.Proxy<IServiceRequestCommandService>((m, a) =>
        {
            var command = a![0]!; Commands.Add(command);
            return command switch
            {
                InitiateServiceRequestCommand => Task.FromResult<RequestId?>(RequestId.NewId()),
                CancelServiceRequestCommand => Task.FromResult(true),
                _ => Task.CompletedTask
            };
        });
        // Query double with the real summary semantics: only the request's own client gets it.
        var queries = AssignmentHttpFixture.Proxy<IServiceDesignQueryService>((m, a) =>
        {
            switch (a![0])
            {
                case GetRequestEligibilityQuery q: QueriedClients.Add(q.Client); return Task.FromResult(new RequestEligibility(true, "BASIC", 3, false, null));
                case GetServiceRequestSummaryQuery q:
                    QueriedClients.Add(q.Client);
                    return Task.FromResult(requests.SingleOrDefault(r => r.RequestId == q.RequestId && r.Client == q.Client));
                case GetAvailableServicesQuery: AvailableServiceQueries++; return Task.FromResult<IEnumerable<AvailableService>>([]);
                default: throw new InvalidOperationException("Unexpected query " + a[0]?.GetType().Name);
            }
        });
        var actors = new OwnerActorDoubles();
        server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            actors.AddTo(services);
            services.AddSingleton(sp => new ExternalProfilesService(sp.GetRequiredService<IProfilesContextFacade>()));
            services.AddSingleton(commands); services.AddSingleton(queries);
            services.AddControllers(o => o.Conventions.Add(new Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Configuration.KebabCaseRouteNamingConvention()))
                .AddApplicationPart(typeof(ServiceRequestController).Assembly).ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlyServiceRequests()));
        }).Configure(app =>
        {
            app.UseRouting(); app.UseMiddleware<RequestAuthorizationMiddleware>();
            // Stale/forged JWT profile claims, as UseAuthentication would expose them: a client role and subject of someone else.
            if (forgedClaims) app.Use(async (context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("businessRole", "HomeOwner"), new Claim("roleSubjectId", OwnerActorDoubles.Attacker)], "synthetic"));
                await next();
            });
            app.UseEndpoints(e => e.MapControllers());
        })); client = server.CreateClient();
    }

    internal Task<HttpResponseMessage> Act(string? token, string action, string requestId)
    {
        var root = "/api/v1/service-requests";
        var path = root + "/" + requestId;
        return action switch
        {
            "eligibility" => Send(HttpMethod.Get, root + "/eligibility", token),
            "initiate" => Send(HttpMethod.Post, root, token),
            "property" => Send(HttpMethod.Post, path + "/property", token, new { propertyId = "prop-synthetic-m329" }),
            "available-services" => Send(HttpMethod.Get, path + "/available-services", token),
            "recipe" => Send(HttpMethod.Post, path + "/recipe", token, new { serviceCategory = "Installation" }),
            "details" => Send(HttpMethod.Post, path + "/details", token, new
            {
                problemDescription = "Synthetic", consumptionKwh = 1m, amountPaid = 1m, amountCurrency = "PEN", billingPeriod = "2026-10",
                receiptNumber = "R-1", isPriority = false, preferredDates = Array.Empty<string>(), timePreference = "Anytime"
            }),
            "confirm" => Send(HttpMethod.Post, path + "/confirm", token),
            "cancel" => Send(HttpMethod.Delete, path, token, new { reason = "CHANGED_MIND" }),
            "summary" => Send(HttpMethod.Get, path, token),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
    }

    internal Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (token == "edge") request.Headers.Add("X-Api-Key", "synthetic-edge-only");
        else if (token != null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        if (body != null) request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    public void Dispose() { client.Dispose(); server.Dispose(); }

    private sealed class OnlyServiceRequests : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        { foreach (var c in feature.Controllers.ToArray()) if (c.AsType() != typeof(ServiceRequestController)) feature.Controllers.Remove(c); }
    }
}
