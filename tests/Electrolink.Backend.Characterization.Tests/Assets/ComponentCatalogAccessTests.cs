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

// M3.25 (user decision): components and component types are a global catalogue without owner.
// Writing is reserved to active Admin/SuperAdmin; reading to active technicians on their own route and to those administrators.
public sealed class ComponentCatalogAccessTests
{
    static readonly string[] Reads = ["types-list", "types-get", "components-list", "components-get"];
    static readonly string[] Writes = ["types-create", "types-update", "types-delete", "types-activate", "types-deactivate",
        "components-create", "components-update", "components-delete"];

    [Theory]
    [InlineData("tech-owner")]
    [InlineData("admin")]
    [InlineData("superadmin")]
    public async Task Active_technician_on_own_route_and_administrators_read_the_catalogue(string token)
    {
        using var h = new ComponentCatalogFixture();
        foreach (var action in Reads)
        {
            using var r = await h.Act(token, action);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }
        Assert.Equal(0, h.Saves);
    }

    [Fact]
    public async Task Another_technicians_route_is_a_generic_404_for_reads()
    {
        using var h = new ComponentCatalogFixture();
        foreach (var action in Reads)
        {
            using var r = await h.Act("tech-other", action);
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            Assert.Equal("{\"message\":\"Resource not found.\"}", await r.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData("tech-suspended")]
    [InlineData("tech-inactive")]
    [InlineData("tech-mismatch")]
    [InlineData("victim")]
    [InlineData("company")]
    [InlineData("no-profile")]
    [InlineData("admin-suspended")]
    public async Task Ineligible_reader_is_403(string token)
    {
        using var h = new ComponentCatalogFixture();
        foreach (var action in Reads)
        {
            using var r = await h.Act(token, action);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
    }

    [Theory]
    [InlineData("tech-owner")]
    [InlineData("tech-other")]
    [InlineData("victim")]
    [InlineData("company")]
    [InlineData("no-profile")]
    [InlineData("admin-suspended")]
    public async Task Only_active_administrators_write_everyone_else_is_403_without_effects(string token)
    {
        using var h = new ComponentCatalogFixture();
        var before = h.Snapshot();
        foreach (var action in Writes)
        {
            using var r = await h.Act(token, action);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        Assert.Equal(before, h.Snapshot()); Assert.Equal(0, h.Saves); Assert.Equal(0, h.Events);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("superadmin")]
    public async Task Administrators_create_update_toggle_and_delete_catalogue_entries(string token)
    {
        using var h = new ComponentCatalogFixture();
        using (var r = await h.Act(token, "types-create")) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var r = await h.Act(token, "components-create")) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(2, h.Added);
        using (var r = await h.Act(token, "types-update")) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Updated type", h.Type.Name);
        using (var r = await h.Act(token, "types-deactivate")) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.False(h.Type.IsActive);
        using (var r = await h.Act(token, "types-activate")) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var r = await h.Act(token, "components-update")) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var r = await h.Act(token, "components-delete")) Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.False(h.Component.IsActive);
        using (var r = await h.Act(token, "types-delete")) Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.True(h.TypeRemoved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("edge")]
    public async Task Missing_or_invalid_token_and_edge_key_are_401(string? token)
    {
        using var h = new ComponentCatalogFixture();
        foreach (var action in new[] { "types-list", "types-create" })
        {
            using var r = await h.Act(token, action);
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }
        Assert.Equal(0, h.Saves);
    }

    [Fact]
    public async Task Administrator_domain_errors_are_409_or_404_not_500()
    {
        using var h = new ComponentCatalogFixture(typeInUse: true);
        using (var r = await h.Send(HttpMethod.Post, ComponentCatalogFixture.Root + "/component-types", "admin", new { name = h.Type.Name, description = "duplicate" }))
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        using (var r = await h.Send(HttpMethod.Post, ComponentCatalogFixture.Root + "/components", "admin", new { name = h.Component.Name, description = "duplicate", isActive = true }))
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        using (var r = await h.Send(HttpMethod.Delete, ComponentCatalogFixture.Root + "/component-types/" + h.Type.Id.Value, "admin"))
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        using (var r = await h.Send(HttpMethod.Put, ComponentCatalogFixture.Root + "/component-types/ctype-missing-m325", "admin", new { name = "x", description = (string?)null, isActive = (bool?)null }))
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        using (var r = await h.Send(HttpMethod.Patch, ComponentCatalogFixture.Root + "/component-types/ctype-missing-m325/activate", "admin"))
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        using (var r = await h.Send(HttpMethod.Put, ComponentCatalogFixture.Root + "/components/cmpt-missing-m325", "admin", new { name = "x", description = (string?)null, isActive = true }))
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal(0, h.Saves);
    }
}

internal sealed class ComponentCatalogFixture : IDisposable
{
    // Reads use the owner technician's route; writes by administrators also go through it (route segment is not an owner).
    internal static readonly string Root = "/api/v1/technicians/" + OwnerActorDoubles.TechOwner;
    internal ComponentType Type { get; } = ComponentType.Create("Synthetic type", "Synthetic");
    internal Component Component { get; } = Component.Create("Synthetic component", "Synthetic", true);
    internal int Saves { get; private set; }
    internal int Events { get; private set; }
    internal int Added { get; private set; }
    internal bool TypeRemoved { get; private set; }
    private readonly TestServer server;
    private readonly HttpClient client;

    internal ComponentCatalogFixture(bool typeInUse = false)
    {
        Type.ClearDomainEvents(); Component.ClearDomainEvents();
        var types = AssignmentHttpFixture.Proxy<IComponentTypeRepository>((m, a) => m.Name switch
        {
            nameof(IComponentTypeRepository.FindByIdAsync) => Task.FromResult<ComponentType?>((ComponentTypeId)a![0]! == Type.Id ? Type : null),
            nameof(IComponentTypeRepository.ListAsync) => Task.FromResult<IEnumerable<ComponentType>>([Type]),
            nameof(IComponentTypeRepository.ExistsByNameAsync) => Task.FromResult((string)a![0]! == Type.Name),
            nameof(IComponentTypeRepository.AddAsync) => Task.Run(() => Added++),
            nameof(IComponentTypeRepository.Remove) => Remove(),
            _ => throw new InvalidOperationException("Unexpected component type call " + m.Name)
        });
        object? Remove() { TypeRemoved = true; return null; }
        var components = AssignmentHttpFixture.Proxy<IComponentRepository>((m, a) => m.Name switch
        {
            nameof(IComponentRepository.FindByIdAsync) => Task.FromResult<Component?>((ComponentId)a![0]! == Component.Id ? Component : null),
            nameof(IComponentRepository.ListAsync) => Task.FromResult<IEnumerable<Component>>([Component]),
            nameof(IComponentRepository.ExistsByNameAsync) => Task.FromResult((string)a![0]! == Component.Name),
            nameof(IComponentRepository.AddAsync) => Task.Run(() => Added++),
            _ => throw new InvalidOperationException("Unexpected component call " + m.Name)
        });
        var inventories = AssignmentHttpFixture.Proxy<ITechnicianInventoryRepository>((m, a) => m.Name == nameof(ITechnicianInventoryRepository.ExistsStockItemsByComponentTypeId)
            ? Task.FromResult(typeInUse) : throw new InvalidOperationException("Unexpected inventory call " + m.Name));
        var unit = AssignmentHttpFixture.Proxy<IUnitOfWork>((m, a) => { Saves++; return Task.CompletedTask; });
        var mediator = AssignmentHttpFixture.Proxy<IMediator>((m, a) => { Events++; return Task.CompletedTask; });
        var actors = new OwnerActorDoubles();
        server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            actors.AddTo(services);
            services.AddSingleton<IComponentTypeCommandService>(new ComponentTypeCommandService(types, inventories, unit, mediator, NullLogger<ComponentTypeCommandService>.Instance));
            services.AddSingleton<IComponentTypeQueryService>(new ComponentTypeQueryService(types));
            services.AddSingleton<IComponentCommandService>(new ComponentCommandService(components, types, unit, mediator, NullLogger<ComponentCommandService>.Instance));
            services.AddSingleton<IComponentQueryService>(new ComponentQueryService(components));
            services.AddControllers(o => o.Conventions.Add(new Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Configuration.KebabCaseRouteNamingConvention()))
                .AddApplicationPart(typeof(ComponentsController).Assembly).ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlyCatalogue()));
        }).Configure(app =>
        {
            app.UseRouting(); app.UseMiddleware<RequestAuthorizationMiddleware>(); app.UseEndpoints(e => e.MapControllers());
        })); client = server.CreateClient();
    }

    internal string Snapshot() => JsonSerializer.Serialize(new { Type = Type.Name, TypeActive = Type.IsActive, Component = Component.Name, ComponentActive = Component.IsActive, Added, TypeRemoved });

    internal Task<HttpResponseMessage> Act(string? token, string action)
    {
        string types = Root + "/component-types", parts = Root + "/components";
        return action switch
        {
            "types-list" => Send(HttpMethod.Get, types, token),
            "types-get" => Send(HttpMethod.Get, types + "/" + Type.Id.Value, token),
            "types-create" => Send(HttpMethod.Post, types, token, new { name = "New type " + token, description = "Synthetic" }),
            "types-update" => Send(HttpMethod.Put, types + "/" + Type.Id.Value, token, new { name = "Updated type", description = (string?)null, isActive = (bool?)null }),
            "types-delete" => Send(HttpMethod.Delete, types + "/" + Type.Id.Value, token),
            "types-activate" => Send(HttpMethod.Patch, types + "/" + Type.Id.Value + "/activate", token),
            "types-deactivate" => Send(HttpMethod.Patch, types + "/" + Type.Id.Value + "/deactivate", token),
            "components-list" => Send(HttpMethod.Get, parts, token),
            "components-get" => Send(HttpMethod.Get, parts + "/" + Component.Id.Value, token),
            "components-create" => Send(HttpMethod.Post, parts, token, new { name = "New component " + token, description = "Synthetic", isActive = true }),
            "components-update" => Send(HttpMethod.Put, parts + "/" + Component.Id.Value, token, new { name = "Updated component", description = (string?)null, isActive = true }),
            "components-delete" => Send(HttpMethod.Delete, parts + "/" + Component.Id.Value, token),
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

    private sealed class OnlyCatalogue : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var c in feature.Controllers.ToArray())
                if (c.AsType() != typeof(ComponentsController) && c.AsType() != typeof(ComponentTypesController)) feature.Controllers.Remove(c);
        }
    }
}
