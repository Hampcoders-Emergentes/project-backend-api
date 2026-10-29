using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Electrolink.Backend.Characterization.Tests.Assets;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Components;
using Hampcoders.Electrolink.API.Planning.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Planning.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Interfaces.REST;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.Planning;

// M3.30: ServiceSuggestionController resolves the client server-side (as M3.29); another client's or a missing suggestion is a
// generic 404, and state conflicts are 409 instead of unhandled 500s.
public sealed class ServiceSuggestionClientTests
{
    const string NotFoundBody = "{\"message\":\"Resource not found.\"}";

    [Fact]
    public async Task Owner_lists_views_accepts_and_dismisses_own_suggestions_ignoring_forged_claims()
    {
        using var h = new ServiceSuggestionFixture(forgedClaims: true);
        using (var r = await h.Send(HttpMethod.Get, "/api/v1/suggestions", "victim"))
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var ids = (await r.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Select(e => e.GetProperty("suggestionId").GetString()).ToArray();
            Assert.Equal(new[] { h.Own.SuggestionId, h.OwnSecond.SuggestionId }.Order(), ids.Order());
        }
        using (var r = await h.Act("victim", "viewed", h.Own.SuggestionId)) Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        using (var r = await h.Act("victim", "accept", h.Own.SuggestionId)) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(ClientIdentity.FromHomeowner(OwnerActorDoubles.Victim), Assert.Single(h.CreatedRequests).Client);
        using (var r = await h.Act("victim", "dismiss", h.OwnSecond.SuggestionId)) Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Equal("Expired", h.OwnSecond.Status.ToString());
    }

    [Theory]
    [InlineData("viewed")]
    [InlineData("accept")]
    [InlineData("dismiss")]
    public async Task Another_clients_or_a_missing_suggestion_is_a_generic_404_without_effects(string action)
    {
        using var h = new ServiceSuggestionFixture();
        var before = h.Snapshot();
        foreach (var id in new[] { h.Foreign.SuggestionId, "missing-m330" })
        {
            using var r = await h.Act("victim", action, id);
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            Assert.Equal(NotFoundBody, await r.Content.ReadAsStringAsync());
        }
        Assert.Equal(before, h.Snapshot()); Assert.Equal(0, h.Saves); Assert.Empty(h.CreatedRequests);
    }

    [Theory]
    [InlineData("inactive-profile")]
    [InlineData("subject-mismatch")]
    [InlineData("no-profile")]
    [InlineData("technician")]
    [InlineData("admin")]
    public async Task Actor_without_an_active_client_profile_is_403_even_with_client_claims(string token)
    {
        using var h = new ServiceSuggestionFixture(forgedClaims: true);
        var before = h.Snapshot();
        using (var r = await h.Send(HttpMethod.Get, "/api/v1/suggestions", token)) Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        foreach (var action in new[] { "viewed", "accept", "dismiss" })
        {
            using var r = await h.Act(token, action, h.Own.SuggestionId);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        Assert.Equal(before, h.Snapshot()); Assert.Equal(0, h.Saves); Assert.Empty(h.CreatedRequests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("edge")]
    public async Task Missing_or_invalid_token_and_edge_key_are_401(string? token)
    {
        using var h = new ServiceSuggestionFixture();
        using var r = await h.Send(HttpMethod.Get, "/api/v1/suggestions", token);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task State_conflicts_are_409_not_500()
    {
        using var h = new ServiceSuggestionFixture();
        using (var r = await h.Act("victim", "accept", h.Own.SuggestionId)) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var saves = h.Saves;
        using (var r = await h.Act("victim", "accept", h.Own.SuggestionId)) Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        using (var r = await h.Act("victim", "viewed", h.Own.SuggestionId)) Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(saves, h.Saves); Assert.Single(h.CreatedRequests);
    }
}

internal sealed class ServiceSuggestionFixture : IDisposable
{
    static ServiceSuggestion Create(string client) => ServiceSuggestion.Create(client, null, "prop-synthetic-m330", "anom-synthetic-m330",
        "dev-synthetic-m330", "OVERCURRENT", "CRITICAL", DateTime.UtcNow.AddDays(1));
    internal ServiceSuggestion Own { get; } = Create(OwnerActorDoubles.Victim);
    internal ServiceSuggestion OwnSecond { get; } = Create(OwnerActorDoubles.Victim);
    internal ServiceSuggestion Foreign { get; } = Create(OwnerActorDoubles.Attacker);
    internal List<ServiceRequest> CreatedRequests { get; } = [];
    internal int Saves { get; private set; }
    private readonly TestServer server;
    private readonly HttpClient client;

    internal ServiceSuggestionFixture(bool forgedClaims = false)
    {
        var all = new[] { Own, OwnSecond, Foreign };
        foreach (var s in all) s.ClearDomainEvents();
        var suggestions = AssignmentHttpFixture.Proxy<IServiceSuggestionRepository>((m, a) => m.Name switch
        {
            nameof(IServiceSuggestionRepository.FindBySuggestionIdAsync) => Task.FromResult(all.SingleOrDefault(s => s.SuggestionId == (string)a![0]!)),
            nameof(IServiceSuggestionRepository.FindActiveByClientIdAsync) => Task.FromResult<IEnumerable<ServiceSuggestion>>(all.Where(s => s.ClientId == (string)a![0]!).ToList()),
            nameof(IServiceSuggestionRepository.Update) => null,
            _ => throw new InvalidOperationException("Unexpected suggestion call " + m.Name)
        });
        var requests = AssignmentHttpFixture.Proxy<IServiceRequestRepository>((m, a) => m.Name == nameof(IServiceRequestRepository.AddAsync)
            ? Task.Run(() => CreatedRequests.Add((ServiceRequest)a![0]!)) : throw new InvalidOperationException("Unexpected request call " + m.Name));
        var unit = AssignmentHttpFixture.Proxy<IUnitOfWork>((m, a) => { Saves++; return Task.CompletedTask; });
        var actors = new OwnerActorDoubles();
        server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            actors.AddTo(services);
            services.AddSingleton(sp => new ExternalProfilesService(sp.GetRequiredService<IProfilesContextFacade>()));
            services.AddSingleton(suggestions); services.AddSingleton(unit);
            services.AddSingleton<IServiceSuggestionCommandService>(new ServiceSuggestionCommandService(suggestions, requests, unit, NullLogger<ServiceSuggestionCommandService>.Instance));
            services.AddSingleton<IServiceSuggestionQueryService>(new ServiceSuggestionQueryService(suggestions));
            services.AddControllers(o => o.Conventions.Add(new Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Configuration.KebabCaseRouteNamingConvention()))
                .AddApplicationPart(typeof(ServiceSuggestionController).Assembly).ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlySuggestions()));
        }).Configure(app =>
        {
            app.UseRouting(); app.UseMiddleware<RequestAuthorizationMiddleware>();
            // Stale/forged JWT profile claims naming another HomeOwner, as UseAuthentication would expose them.
            if (forgedClaims) app.Use(async (context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("businessRole", "HomeOwner"), new Claim("roleSubjectId", OwnerActorDoubles.Attacker)], "synthetic"));
                await next();
            });
            app.UseEndpoints(e => e.MapControllers());
        })); client = server.CreateClient();
    }

    internal string Snapshot() => JsonSerializer.Serialize(new[] { Own, OwnSecond, Foreign }.Select(s => s.SuggestionId + ":" + s.Status));

    internal Task<HttpResponseMessage> Act(string? token, string action, string suggestionId) => action switch
    {
        "viewed" => Send(HttpMethod.Patch, "/api/v1/suggestions/" + suggestionId + "/viewed", token),
        "accept" => Send(HttpMethod.Post, "/api/v1/suggestions/" + suggestionId + "/accept", token),
        "dismiss" => Send(HttpMethod.Post, "/api/v1/suggestions/" + suggestionId + "/dismiss", token),
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    internal Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token)
    {
        var request = new HttpRequestMessage(method, path);
        if (token == "edge") request.Headers.Add("X-Api-Key", "synthetic-edge-only");
        else if (token != null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        return client.SendAsync(request);
    }

    public void Dispose() { client.Dispose(); server.Dispose(); }

    private sealed class OnlySuggestions : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        { foreach (var c in feature.Controllers.ToArray()) if (c.AsType() != typeof(ServiceSuggestionController)) feature.Controllers.Remove(c); }
    }
}
