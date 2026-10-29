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
using Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Queries;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Components;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Infrastructure;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.Assets;

// M3.22: the route owner must be the authenticated actor's own Homeowner/Company subject (inverts the M3.21 characterization).
public sealed class PropertyOwnershipTests
{
    const string NotFoundBody = "{\"message\":\"Resource not found.\"}";

    [Fact]
    public async Task Owner_lists_reads_creates_mutates_and_signs_photos_for_own_properties()
    {
        using var h = new PropertyOwnershipFixture();
        var own = h.Route(PropertyOwnershipFixture.Victim);
        var id = h.VictimProperty.Id.Value;
        using (var r = await h.Send(HttpMethod.Get, own, "victim"))
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal(id, Assert.Single((await r.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()).GetProperty("propertyId").GetString());
        }
        using (var r = await h.Send(HttpMethod.Get, own + "/" + id, "victim")) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var r = await h.Send(HttpMethod.Patch, own + "/" + id + "/deactivate", "victim")) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.False(h.VictimProperty.IsActive);
        using (var r = await h.Send(HttpMethod.Patch, own + "/" + id + "/activate", "victim")) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var r = await h.Send(HttpMethod.Patch, own + "/" + id + "/location", "victim", PropertyOwnershipFixture.Location)) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(1.5, h.VictimProperty.Geolocation.Latitude);
        using (var r = await h.Send(HttpMethod.Get, own + "/" + id + "/photos/upload-url", "victim")) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(1, h.SignedUrls);
        using (var r = await h.Send(HttpMethod.Post, own, "victim", PropertyOwnershipFixture.NewProperty)) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(ClientIdentity.FromHomeowner(PropertyOwnershipFixture.Victim), Assert.Single(h.Added).Owner);
        // Archiving twice stays idempotent for the owner.
        using (var r = await h.Send(HttpMethod.Delete, own + "/" + id + "?reason=synthetic", "victim")) Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        using (var r = await h.Send(HttpMethod.Delete, own + "/" + id + "?reason=synthetic", "victim")) Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Equal(EPropertyStatus.Archived, h.VictimProperty.Status);
    }

    [Fact]
    public async Task Company_owner_uses_the_companies_route()
    {
        using var h = new PropertyOwnershipFixture();
        using var r = await h.Send(HttpMethod.Get, "/api/v1/companies/" + PropertyOwnershipFixture.Company + "/properties/" + h.CompanyProperty.Id.Value, "company");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Theory]
    // Another owner named in the route.
    [InlineData("attacker", "victim-route", "list")]
    [InlineData("attacker", "victim-route", "create")]
    [InlineData("attacker", "victim-route", "photo-url")]
    // Own route, another owner's property.
    [InlineData("attacker", "attacker-route", "get")]
    [InlineData("attacker", "attacker-route", "deactivate")]
    [InlineData("attacker", "attacker-route", "activate")]
    [InlineData("attacker", "attacker-route", "location")]
    [InlineData("attacker", "attacker-route", "archive")]
    [InlineData("attacker", "attacker-route", "photo-url")]
    // Route prefix that does not match the actor's role.
    [InlineData("victim", "victim-as-company-route", "get")]
    [InlineData("company", "company-as-homeowner-route", "list")]
    public async Task Foreign_owner_or_property_is_a_generic_404_without_effects(string token, string route, string action)
    {
        using var h = new PropertyOwnershipFixture();
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
        using var h = new PropertyOwnershipFixture();
        var before = h.Snapshot();
        foreach (var action in new[] { "list", "get", "deactivate", "create" })
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
        using var h = new PropertyOwnershipFixture();
        var before = h.Snapshot();
        using var response = await h.Act(token, "victim-route", "list");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        h.AssertUnchanged(before);
    }

    [Fact]
    public async Task Owner_gets_404_not_500_for_a_missing_property()
    {
        using var h = new PropertyOwnershipFixture();
        foreach (var suffix in new[] { "?reason=synthetic", "/photos/upload-url", "/location" })
        {
            var method = suffix.StartsWith('?') ? HttpMethod.Delete : suffix == "/location" ? HttpMethod.Patch : HttpMethod.Get;
            using var r = await h.Send(method, h.Route(PropertyOwnershipFixture.Victim) + "/prop-missing-m322" + suffix, "victim",
                suffix == "/location" ? PropertyOwnershipFixture.Location : null);
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        }
        Assert.Equal(0, h.Saves);
    }
}

internal sealed class PropertyOwnershipFixture : IDisposable
{
    internal const string Victim = "ho-victim-m322";
    internal const string Attacker = "ho-attacker-m322";
    internal const string Company = "comp-owner-m322";
    internal static readonly object Location = new { geolocation = new { latitude = 1.5, longitude = 2.5, accuracy = (int?)null, source = "MANUAL" } };
    internal static readonly object NewProperty = new
    {
        address = new { street = "Synthetic", number = "1", district = "Synthetic", city = "Synthetic", country = "Synthetic", postalCode = "00000" },
        geolocation = new { latitude = 0d, longitude = 0d, accuracy = (int?)null, source = "MANUAL" }, propertyType = "Commercial"
    };
    static Property Create(ClientIdentity owner) => Property.Create(owner,
        Address.Create("Synthetic", "1", "Synthetic", "Synthetic", "Synthetic", "00000"), Geolocation.Create(-12.05, -77.04, null, "SYNTHETIC"));
    internal Property VictimProperty { get; } = Create(ClientIdentity.FromHomeowner(Victim));
    internal Property CompanyProperty { get; } = Create(ClientIdentity.FromCompany(Company));
    internal List<Property> Added { get; } = [];
    internal int Saves { get; private set; }
    internal int Events { get; private set; }
    internal int SignedUrls { get; private set; }
    private readonly TestServer server;
    private readonly HttpClient client;

    internal PropertyOwnershipFixture()
    {
        User Make(string name, AccessRole role = AccessRole.User) => User.Create(Email.From("m322-" + name + "@example.invalid"), HashedPassword.FromHash("synthetic"), role);
        var users = new Dictionary<string, User>
        {
            ["victim"] = Make("victim"), ["attacker"] = Make("attacker"), ["company"] = Make("company"), ["suspended"] = Make("suspended"),
            ["no-profile"] = Make("no-profile"), ["inactive-profile"] = Make("inactive"), ["technician"] = Make("technician"),
            ["subject-mismatch"] = Make("mismatch"), ["admin"] = Make("admin", AccessRole.Admin)
        };
        users["suspended"].Suspend("synthetic");
        // Profile of each user: (status, role, subject). The suspended user keeps a valid, concordant profile of its own.
        var claims = new Dictionary<string, (string Status, string Role, string Subject)?>
        {
            ["victim"] = ("Active", "HomeOwner", Victim), ["attacker"] = ("Active", "HomeOwner", Attacker), ["company"] = ("Active", "Company", Company),
            ["suspended"] = ("Active", "HomeOwner", "ho-suspended-m322"), ["no-profile"] = null, ["inactive-profile"] = ("Deactivated", "HomeOwner", Victim),
            ["technician"] = ("Active", "Technician", "tech-m322"), ["subject-mismatch"] = ("Active", "HomeOwner", Victim), ["admin"] = null
        };
        var tokens = AssignmentHttpFixture.Proxy<ITokenService>((m, a) =>
            Task.FromResult(users.TryGetValue((string)a![0]!, out var u) ? u.Id.Value : null));
        var userQueries = AssignmentHttpFixture.Proxy<IUserQueryService>((m, a) =>
            a![0] is GetUserByIdQuery q ? Task.FromResult<User?>(users.Values.SingleOrDefault(u => u.Id.Value == q.UserId)) : throw new InvalidOperationException("Unexpected user call"));
        string? KeyOf(string userId) => users.SingleOrDefault(p => p.Value.Id.Value == userId).Key;
        var profiles = AssignmentHttpFixture.Proxy<IProfilesContextFacade>((m, a) =>
        {
            var arg = (string)a![0]!;
            return m.Name switch
            {
                nameof(IProfilesContextFacade.GetProfileClaimsAsync) => Task.FromResult<(string ProfileId, string ProfileStatus, string? BusinessRole, string? RoleSubjectId, string? SubscriptionTier)?>(
                    KeyOf(arg) is { } key && claims[key] is { } c ? ("prof-" + key, c.Status, c.Role, c.Subject, null) : null),
                // The real owner of each subject's profile; "subject-mismatch" claims the victim's subject from another profile.
                nameof(IProfilesContextFacade.GetProfileIdByHomeownerIdAsync) => Task.FromResult<string?>(arg switch { Victim => "prof-victim", Attacker => "prof-attacker", "ho-suspended-m322" => "prof-suspended", _ => null }),
                nameof(IProfilesContextFacade.GetProfileIdByCompanyIdAsync) => Task.FromResult<string?>(arg == Company ? "prof-company" : null),
                _ => throw new InvalidOperationException("Unexpected profile call " + m.Name)
            };
        });
        // Mirrors PropertyRepository: owner-filtered queries filter, FindByIdAsync does not.
        bool Owned(Property p, ClientIdentity o) => p.Owner.ClientType == o.ClientType && p.Owner.ClientId == o.ClientId;
        var all = new List<Property> { VictimProperty, CompanyProperty };
        var properties = AssignmentHttpFixture.Proxy<IPropertyRepository>((m, a) => m.Name switch
        {
            nameof(IPropertyRepository.FindByIdAsync) => Task.FromResult<Property?>(all.SingleOrDefault(p => p.Id == (PropertyId)a![0]!)),
            nameof(IPropertyRepository.FindByIdAndOwnerAsync) => Task.FromResult<Property?>(all.SingleOrDefault(p => p.Id == (PropertyId)a![0]! && Owned(p, (ClientIdentity)a[1]!))),
            nameof(IPropertyRepository.GetAllFilteredPaginatedAsync) => Task.FromResult<(IEnumerable<Property> Items, int TotalCount)>(
                (all.Where(p => Owned(p, (ClientIdentity)a![0]!)).ToList(), all.Count(p => Owned(p, (ClientIdentity)a[0]!)))),
            nameof(IPropertyRepository.AddAsync) => Task.Run(() => { Added.Add((Property)a![0]!); all.Add((Property)a[0]!); }),
            _ => throw new InvalidOperationException("Unexpected property call " + m.Name)
        });
        var unit = AssignmentHttpFixture.Proxy<IUnitOfWork>((m, a) => { Saves++; return Task.CompletedTask; });
        var mediator = AssignmentHttpFixture.Proxy<IMediator>((m, a) => { Events++; return Task.CompletedTask; });
        // Synthetic signature only; no storage provider is contacted.
        var files = AssignmentHttpFixture.Proxy<IFileStorageService>((m, a) =>
        {
            if (m.Name != nameof(IFileStorageService.GetSignedUploadUrlForPropertyPhotoAsync)) throw new InvalidOperationException("Unexpected storage call " + m.Name);
            SignedUrls++; return Task.FromResult(new SignedUploadData("https://storage.invalid/upload", "synthetic", 0, "synthetic"));
        });
        server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton(tokens); services.AddSingleton(userQueries); services.AddSingleton(profiles);
            services.AddSingleton<IPropertyCommandService>(new PropertyCommandService(properties, unit, mediator, files, null!));
            services.AddSingleton<IPropertyQueryService>(new PropertyQueryService(properties));
            services.AddControllers(o => o.Conventions.Add(new Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Configuration.KebabCaseRouteNamingConvention()))
                .AddApplicationPart(typeof(PropertiesController).Assembly).ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlyProperties()));
        }).Configure(app =>
        {
            app.UseRouting(); app.UseMiddleware<RequestAuthorizationMiddleware>(); app.UseEndpoints(e => e.MapControllers());
        })); client = server.CreateClient();
    }

    internal string Route(string owner, string type = "homeowners") => "/api/v1/" + type + "/" + owner + "/properties";

    internal string Snapshot() => JsonSerializer.Serialize(new
    {
        VictimProperty.IsActive, Status = VictimProperty.Status.ToString(), VictimProperty.Geolocation.Latitude, Count = Added.Count
    });

    internal void AssertUnchanged(string before)
    {
        Assert.Equal(before, Snapshot()); Assert.Equal(0, Saves); Assert.Equal(0, Events); Assert.Equal(0, SignedUrls);
    }

    internal Task<HttpResponseMessage> Act(string? token, string route, string action)
    {
        var root = route switch
        {
            "victim-route" => Route(Victim), "attacker-route" => Route(Attacker),
            "victim-as-company-route" => Route(Victim, "companies"), "company-as-homeowner-route" => Route(Company),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };
        var id = route == "company-as-homeowner-route" ? CompanyProperty.Id.Value : VictimProperty.Id.Value;
        return action switch
        {
            "list" => Send(HttpMethod.Get, root, token),
            "create" => Send(HttpMethod.Post, root, token, NewProperty),
            "get" => Send(HttpMethod.Get, root + "/" + id, token),
            "deactivate" => Send(HttpMethod.Patch, root + "/" + id + "/deactivate", token),
            "activate" => Send(HttpMethod.Patch, root + "/" + id + "/activate", token),
            "location" => Send(HttpMethod.Patch, root + "/" + id + "/location", token, Location),
            "archive" => Send(HttpMethod.Delete, root + "/" + id + "?reason=synthetic", token),
            "photo-url" => Send(HttpMethod.Get, root + "/" + id + "/photos/upload-url", token),
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

    private sealed class OnlyProperties : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        { foreach (var c in feature.Controllers.ToArray()) if (c.AsType() != typeof(PropertiesController)) feature.Controllers.Remove(c); }
    }
}
