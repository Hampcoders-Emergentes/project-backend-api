using System.Net;
using System.Net.Http.Json;
using Electrolink.Backend.Characterization.Tests.Assets;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Components;
using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Entities;
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

// M3.26 (user decision): a technician's service catalog is read and written only by that technician; "me" resolves to the actor.
public sealed class ServiceCatalogOwnershipTests
{
    static readonly string[] Actions = ["get", "create-catalog", "recipe-get", "recipe-create", "recipe-update", "recipe-deactivate", "recipe-reactivate"];

    [Theory]
    [InlineData(OwnerActorDoubles.TechOwner)]
    [InlineData("me")]
    public async Task Technician_uses_own_catalog_by_id_or_me_and_commands_carry_the_resolved_technician(string route)
    {
        using var h = new ServiceCatalogFixture();
        foreach (var action in Actions)
        {
            using var r = await h.Act("tech-owner", action, route);
            Assert.True(r.IsSuccessStatusCode, action + " returned " + (int)r.StatusCode);
        }
        Assert.Equal(5, h.Commands.Count);
        Assert.All(h.Commands, c => Assert.Equal(OwnerActorDoubles.TechOwner, ServiceCatalogFixture.TechnicianOf(c)));
        Assert.Equal(ProfileId.From("prof-tech-owner"), Assert.IsType<CreateServiceCatalogCommand>(h.Commands[0]).ProfileId);
    }

    [Fact]
    public async Task Another_technicians_catalog_is_a_generic_404_without_commands()
    {
        using var h = new ServiceCatalogFixture();
        foreach (var action in Actions)
        {
            using var r = await h.Act("tech-other", action, OwnerActorDoubles.TechOwner);
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            Assert.Equal("{\"message\":\"Resource not found.\"}", await r.Content.ReadAsStringAsync());
        }
        Assert.Empty(h.Commands);
    }

    [Theory]
    [InlineData("tech-suspended")]
    [InlineData("tech-inactive")]
    [InlineData("tech-mismatch")]
    [InlineData("victim")]
    [InlineData("company")]
    [InlineData("admin")]
    [InlineData("no-profile")]
    public async Task Actor_without_an_eligible_technician_profile_is_403_without_commands(string token)
    {
        using var h = new ServiceCatalogFixture();
        foreach (var route in new[] { OwnerActorDoubles.TechOwner, "me" })
            foreach (var action in Actions)
            {
                using var r = await h.Act(token, action, route);
                Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            }
        Assert.Empty(h.Commands);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("edge")]
    public async Task Missing_or_invalid_token_and_edge_key_are_401(string? token)
    {
        using var h = new ServiceCatalogFixture();
        foreach (var action in new[] { "get", "recipe-create" })
        {
            using var r = await h.Act(token, action, OwnerActorDoubles.TechOwner);
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }
        Assert.Empty(h.Commands);
    }
}

internal sealed class ServiceCatalogFixture : IDisposable
{
    internal List<object> Commands { get; } = [];
    private readonly ServiceCatalog owner = ServiceCatalog.Create(TechnicianId.From(OwnerActorDoubles.TechOwner));
    private readonly ServiceCatalog other = ServiceCatalog.Create(TechnicianId.From(OwnerActorDoubles.TechOther));
    private readonly string recipeId;
    private readonly TestServer server;
    private readonly HttpClient client;

    internal static string TechnicianOf(object command) => command switch
    {
        CreateServiceCatalogCommand c => c.TechnicianId.Value, CreateServiceRecipeCommand c => c.TechnicianId.Value,
        UpdateServiceRecipeCommand c => c.TechnicianId.Value, DeactivateServiceRecipeCommand c => c.TechnicianId.Value,
        ReactivateServiceRecipeCommand c => c.TechnicianId.Value, _ => throw new ArgumentOutOfRangeException(nameof(command))
    };

    internal ServiceCatalogFixture()
    {
        foreach (var catalog in new[] { owner, other })
        {
            catalog.AddRecipe("Synthetic service", "Synthetic", EServiceCategory.Installation, false, [], EstimatedDuration.FromMinutes(30),
                ServicePricing.Create(Money.Of(0, ECurrency.Pen), Money.Of(1, ECurrency.Pen), Money.Of(1, ECurrency.Pen)), [], [], WarrantyPeriod.OfMonths(0), null!);
            catalog.ClearDomainEvents();
        }
        recipeId = owner.Recipes.Single().Id.Value;
        // Command service double: records what reaches it; the real handlers are unchanged by this cut.
        var commands = AssignmentHttpFixture.Proxy<IServiceCatalogCommandService>((m, a) =>
        {
            var command = a![0]!; Commands.Add(command);
            return command switch
            {
                CreateServiceCatalogCommand => Task.FromResult<ServiceCatalog?>(owner),
                CreateServiceRecipeCommand or UpdateServiceRecipeCommand => Task.FromResult<ServiceRecipe?>(owner.Recipes.Single()),
                _ => Task.FromResult(true)
            };
        });
        var queries = AssignmentHttpFixture.Proxy<IServiceDesignQueryService>((m, a) => a![0] is GetServiceCatalogQuery q
            ? Task.FromResult<ServiceCatalog?>(new[] { owner, other }.SingleOrDefault(c => c.TechnicianId == q.TechnicianId))
            : throw new InvalidOperationException("Unexpected query " + a[0]?.GetType().Name));
        var actors = new OwnerActorDoubles();
        server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            actors.AddTo(services);
            services.AddSingleton(sp => new ExternalProfilesService(sp.GetRequiredService<IProfilesContextFacade>()));
            services.AddSingleton(commands); services.AddSingleton(queries);
            services.AddControllers(o => o.Conventions.Add(new Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Configuration.KebabCaseRouteNamingConvention()))
                .AddApplicationPart(typeof(ServiceCatalogController).Assembly).ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlyCatalog()));
        }).Configure(app =>
        {
            app.UseRouting(); app.UseMiddleware<RequestAuthorizationMiddleware>(); app.UseEndpoints(e => e.MapControllers());
        })); client = server.CreateClient();
    }

    internal Task<HttpResponseMessage> Act(string? token, string action, string technician)
    {
        var root = "/api/v1/technicians/" + technician + "/catalog";
        var pricing = new { materialsEstimate = 0m, laborCost = 1m, totalPrice = 1m, currency = "PEN" };
        return action switch
        {
            "get" => Send(HttpMethod.Get, root, token),
            "create-catalog" => Send(HttpMethod.Post, root, token),
            "recipe-get" => Send(HttpMethod.Get, root + "/recipes/" + recipeId, token),
            "recipe-create" => Send(HttpMethod.Post, root + "/recipes", token, new
            {
                serviceName = "New service", serviceDescription = "Synthetic", serviceCategory = "Installation", requiresIoTCertification = false,
                componentRequirements = Array.Empty<object>(), estimatedDurationHours = 0, estimatedDurationMinutes = 30, pricing,
                prerequisites = Array.Empty<string>(), deliverables = Array.Empty<string>(), warrantyMonths = 0
            }),
            "recipe-update" => Send(HttpMethod.Patch, root + "/recipes/" + recipeId, token, new { serviceName = "Renamed service" }),
            "recipe-deactivate" => Send(HttpMethod.Delete, root + "/recipes/" + recipeId, token, new { reason = "SEASONAL" }),
            "recipe-reactivate" => Send(HttpMethod.Post, root + "/recipes/" + recipeId + "/reactivate", token),
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

    private sealed class OnlyCatalog : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        { foreach (var c in feature.Controllers.ToArray()) if (c.AsType() != typeof(ServiceCatalogController)) feature.Controllers.Remove(c); }
    }
}
