using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hampcoders.Electrolink.API.Assets.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Assets.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Assets.Interfaces.REST;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Components;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.Assets;

// M3.23: PropertyPortfoliosController binds the route owner to the authenticated actor, like PropertiesController (M3.22).
public sealed class PortfolioOwnershipTests
{
    const string NotFoundBody = "{\"message\":\"Resource not found.\"}";

    [Fact]
    public async Task Owner_reads_adds_and_removes_in_own_portfolio()
    {
        using var h = new PortfolioOwnershipFixture();
        var root = PortfolioOwnershipFixture.Route(OwnerActorDoubles.Victim);
        using (var r = await h.Send(HttpMethod.Get, root, "victim"))
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var entry = Assert.Single((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("entries").EnumerateArray());
            Assert.Equal(h.Housed.Id.Value, entry.GetProperty("propertyId").GetString());
        }
        using (var r = await h.Send(HttpMethod.Post, root + "/properties", "victim", h.AddBody(h.Spare))) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains(h.VictimPortfolio.Entries, e => e.PropertyId == h.Spare.Id);
        using (var r = await h.Send(HttpMethod.Delete, root + "/properties/" + h.Housed.Id.Value + "?reason=synthetic", "victim")) Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.DoesNotContain(h.VictimPortfolio.Entries, e => e.PropertyId == h.Housed.Id);
    }

    [Fact]
    public async Task Company_owner_reads_its_portfolio_on_the_companies_route()
    {
        using var h = new PortfolioOwnershipFixture();
        using var r = await h.Send(HttpMethod.Get, PortfolioOwnershipFixture.Route(OwnerActorDoubles.Company, "companies"), "company");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Theory]
    [InlineData("attacker", "victim-route", "get")]
    [InlineData("attacker", "victim-route", "add")]
    [InlineData("attacker", "victim-route", "remove")]
    [InlineData("attacker", "attacker-route", "add")]          // another owner's property into the actor's own portfolio
    [InlineData("victim", "victim-as-company-route", "get")]
    [InlineData("company", "company-as-homeowner-route", "get")]
    public async Task Foreign_owner_or_property_is_a_generic_404_without_effects(string token, string route, string action)
    {
        using var h = new PortfolioOwnershipFixture();
        var before = h.Snapshot();
        using var response = await h.Act(token, route, action);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(NotFoundBody, await response.Content.ReadAsStringAsync());
        h.AssertUnchanged(before);
    }

    [Theory]
    [InlineData("suspended")]
    [InlineData("no-profile")]
    [InlineData("inactive-profile")]
    [InlineData("technician")]
    [InlineData("subject-mismatch")]
    [InlineData("admin")]
    public async Task Actor_without_an_eligible_client_profile_is_403_without_effects(string token)
    {
        using var h = new PortfolioOwnershipFixture();
        var before = h.Snapshot();
        foreach (var action in new[] { "get", "add", "remove" })
        {
            using var response = await h.Act(token, "victim-route", action);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        h.AssertUnchanged(before);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("edge")]
    public async Task Missing_or_invalid_token_and_edge_key_are_401(string? token)
    {
        using var h = new PortfolioOwnershipFixture();
        var before = h.Snapshot();
        using var response = await h.Act(token, "victim-route", "get");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        h.AssertUnchanged(before);
    }

    [Fact]
    public async Task Owner_gets_404_not_500_for_a_missing_property_or_entry()
    {
        using var h = new PortfolioOwnershipFixture();
        var root = PortfolioOwnershipFixture.Route(OwnerActorDoubles.Victim);
        using (var r = await h.Send(HttpMethod.Post, root + "/properties", "victim", new { propertyId = "prop-missing-m323", nickname = "Missing", isPrimary = false, occupancyStatus = "Vacant" }))
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        using (var r = await h.Send(HttpMethod.Delete, root + "/properties/" + h.Spare.Id.Value + "?reason=synthetic", "victim"))
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal(0, h.Saves);
    }
}

internal sealed class PortfolioOwnershipFixture : IDisposable
{
    static Property NewProperty(ClientIdentity owner) => Property.Create(owner,
        Address.Create("Synthetic", "1", "Synthetic", "Synthetic", "Synthetic", "00000"), Geolocation.Create(-12.05, -77.04, null, "SYNTHETIC"));
    internal Property Housed { get; } = NewProperty(ClientIdentity.FromHomeowner(OwnerActorDoubles.Victim));
    internal Property Spare { get; } = NewProperty(ClientIdentity.FromHomeowner(OwnerActorDoubles.Victim));
    internal PropertyPortfolio VictimPortfolio { get; } = PropertyPortfolio.Create(ClientIdentity.FromHomeowner(OwnerActorDoubles.Victim));
    internal int Saves { get; private set; }
    internal int Events { get; private set; }
    private readonly TestServer server;
    private readonly HttpClient client;

    internal PortfolioOwnershipFixture()
    {
        VictimPortfolio.AddProperty(Housed.Id, "Home", true, EOccupancyStatus.OwnerOccupied);
        VictimPortfolio.ClearDomainEvents();
        var portfolios = new[] { VictimPortfolio, PropertyPortfolio.Create(ClientIdentity.FromHomeowner(OwnerActorDoubles.Attacker)),
            PropertyPortfolio.Create(ClientIdentity.FromCompany(OwnerActorDoubles.Company)) };
        foreach (var p in portfolios) p.ClearDomainEvents();
        var all = new[] { Housed, Spare };
        var portfolioRepository = AssignmentHttpFixture.Proxy<IPropertyPortfolioRepository>((m, a) => m.Name switch
        {
            nameof(IPropertyPortfolioRepository.FindByOwnerWithEntriesAsync) or nameof(IPropertyPortfolioRepository.FindByOwnerAsync) =>
                Task.FromResult<PropertyPortfolio?>(portfolios.SingleOrDefault(p => p.Owner == (ClientIdentity)a![0]!)),
            nameof(IPropertyPortfolioRepository.Update) => null,
            _ => throw new InvalidOperationException("Unexpected portfolio call " + m.Name)
        });
        var propertyRepository = AssignmentHttpFixture.Proxy<IPropertyRepository>((m, a) => m.Name == nameof(IPropertyRepository.FindByIdAsync)
            ? Task.FromResult<Property?>(all.SingleOrDefault(p => p.Id == (PropertyId)a![0]!))
            : throw new InvalidOperationException("Unexpected property call " + m.Name));
        var unit = AssignmentHttpFixture.Proxy<IUnitOfWork>((m, a) => { Saves++; return Task.CompletedTask; });
        var mediator = AssignmentHttpFixture.Proxy<IMediator>((m, a) => { Events++; return Task.CompletedTask; });
        var actors = new OwnerActorDoubles();
        server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            actors.AddTo(services);
            services.AddSingleton<IPropertyPortfolioCommandService>(new PropertyPortfolioCommandService(portfolioRepository, propertyRepository, unit, mediator,
                NullLogger<PropertyPortfolioCommandService>.Instance));
            services.AddSingleton<IPropertyPortfolioQueryService>(new PropertyPortfolioQueryService(portfolioRepository, unit, mediator));
            services.AddControllers(o => o.Conventions.Add(new Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Configuration.KebabCaseRouteNamingConvention()))
                .AddApplicationPart(typeof(PropertyPortfoliosController).Assembly).ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlyPortfolios()));
        }).Configure(app =>
        {
            app.UseRouting(); app.UseMiddleware<RequestAuthorizationMiddleware>(); app.UseEndpoints(e => e.MapControllers());
        })); client = server.CreateClient();
    }

    internal static string Route(string owner, string type = "homeowners") => "/api/v1/" + type + "/" + owner + "/property-portfolios";

    internal object AddBody(Property property) => new { propertyId = property.Id.Value, nickname = "Synthetic " + property.Id.Value, isPrimary = false, occupancyStatus = "Vacant" };

    internal string Snapshot() => JsonSerializer.Serialize(new
    {
        Entries = VictimPortfolio.Entries.Select(e => e.PropertyId.Value).OrderBy(v => v).ToArray(),
        Housed = Housed.Status.ToString(), Spare = Spare.Status.ToString()
    });

    internal void AssertUnchanged(string before)
    {
        Assert.Equal(before, Snapshot()); Assert.Equal(0, Saves); Assert.Equal(0, Events);
    }

    internal Task<HttpResponseMessage> Act(string? token, string route, string action)
    {
        var root = route switch
        {
            "victim-route" => Route(OwnerActorDoubles.Victim), "attacker-route" => Route(OwnerActorDoubles.Attacker),
            "victim-as-company-route" => Route(OwnerActorDoubles.Victim, "companies"), "company-as-homeowner-route" => Route(OwnerActorDoubles.Company),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };
        return action switch
        {
            "get" => Send(HttpMethod.Get, root, token),
            "add" => Send(HttpMethod.Post, root + "/properties", token, AddBody(Spare)),
            "remove" => Send(HttpMethod.Delete, root + "/properties/" + Housed.Id.Value + "?reason=synthetic", token),
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

    private sealed class OnlyPortfolios : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        { foreach (var c in feature.Controllers.ToArray()) if (c.AsType() != typeof(PropertyPortfoliosController)) feature.Controllers.Remove(c); }
    }
}
