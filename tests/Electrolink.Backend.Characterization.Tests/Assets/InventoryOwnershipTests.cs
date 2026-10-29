using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hampcoders.Electrolink.API.Assets.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Assets.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Entities;
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
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.Assets;

// M3.24: TechnicianInventoriesController binds the route technicianId to the authenticated technician.
public sealed class InventoryOwnershipTests
{
    const string NotFoundBody = "{\"message\":\"Resource not found.\"}";

    [Fact]
    public async Task Technician_reads_and_adjusts_own_inventory()
    {
        using var h = new InventoryOwnershipFixture();
        var root = InventoryOwnershipFixture.Route(OwnerActorDoubles.TechOwner);
        using (var r = await h.Send(HttpMethod.Get, root, "tech-owner")) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var r = await h.Send(HttpMethod.Get, root + "/stock-items", "tech-owner"))
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal(InventoryOwnershipFixture.Component, Assert.Single((await r.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()).GetProperty("componentId").GetString());
        }
        using (var r = await h.Send(HttpMethod.Patch, root + "/" + InventoryOwnershipFixture.Component + "/increase", "tech-owner", new { amount = 3 }))
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(8, h.Quantity());
        using (var r = await h.Send(HttpMethod.Patch, root + "/" + InventoryOwnershipFixture.Component + "/decrease", "tech-owner", new { amount = 2 }))
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(6, h.Quantity());
    }

    [Fact]
    public async Task Creating_an_existing_own_inventory_is_409_not_500()
    {
        using var h = new InventoryOwnershipFixture();
        using var r = await h.Send(HttpMethod.Post, InventoryOwnershipFixture.Route(OwnerActorDoubles.TechOwner), "tech-owner");
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(0, h.Saves);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("stock-items")]
    [InlineData("create")]
    [InlineData("add-stock")]
    [InlineData("update")]
    [InlineData("remove")]
    [InlineData("increase")]
    [InlineData("decrease")]
    public async Task Another_technicians_route_is_a_generic_404_without_effects(string action)
    {
        using var h = new InventoryOwnershipFixture();
        var before = h.Snapshot();
        using var response = await h.Act("tech-other", action);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(NotFoundBody, await response.Content.ReadAsStringAsync());
        h.AssertUnchanged(before);
    }

    [Theory]
    [InlineData("tech-suspended")]
    [InlineData("tech-inactive")]
    [InlineData("tech-mismatch")]
    [InlineData("victim")]
    [InlineData("company")]
    [InlineData("admin")]
    [InlineData("no-profile")]
    public async Task Actor_without_an_eligible_technician_profile_is_403_without_effects(string token)
    {
        using var h = new InventoryOwnershipFixture();
        var before = h.Snapshot();
        foreach (var action in new[] { "get", "create", "increase", "remove" })
        {
            using var response = await h.Act(token, action);
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
        using var h = new InventoryOwnershipFixture();
        var before = h.Snapshot();
        using var response = await h.Act(token, "increase");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        h.AssertUnchanged(before);
    }

    [Fact]
    public async Task Own_missing_component_or_stock_is_404_not_500()
    {
        using var h = new InventoryOwnershipFixture();
        var root = InventoryOwnershipFixture.Route(OwnerActorDoubles.TechOwner);
        using (var r = await h.Send(HttpMethod.Post, root + "/stock-items", "tech-owner", new { componentId = "cmpt-missing-m324", componentTypeId = "ctype-missing-m324", quantity = 1, alertThreshold = 0 }))
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        using (var r = await h.Send(HttpMethod.Put, root + "/" + "cmpt-missing-m324", "tech-owner", new { newQuantity = 1, newAlertThreshold = 0 }))
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        using (var r = await h.Send(HttpMethod.Delete, root + "/" + "cmpt-missing-m324", "tech-owner"))
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal(0, h.Saves);
    }
}

internal sealed class InventoryOwnershipFixture : IDisposable
{
    internal const string Component = "cmpt-owner-m324";
    internal int Saves { get; private set; }
    internal int Events { get; private set; }
    private readonly TechnicianInventory ownerInventory = TechnicianInventory.Create(TechnicianId.From(OwnerActorDoubles.TechOwner));
    private readonly List<TechnicianInventory> inventories;
    private readonly TestServer server;
    private readonly HttpClient client;

    internal InventoryOwnershipFixture()
    {
        // AddStock is internal to the aggregate; seed the owner's stock through it by reflection (test setup only).
        typeof(TechnicianInventory).GetMethod("AddStock", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(ownerInventory, [ComponentId.From(Component), ComponentTypeId.From("ctype-owner-m324"), 5, 1]);
        var otherInventory = TechnicianInventory.Create(TechnicianId.From(OwnerActorDoubles.TechOther));
        inventories = [ownerInventory, otherInventory];
        foreach (var i in inventories) i.ClearDomainEvents();
        var inventoryRepository = AssignmentHttpFixture.Proxy<ITechnicianInventoryRepository>((m, a) => m.Name switch
        {
            nameof(ITechnicianInventoryRepository.FindByTechnicianIdAsync) => Task.FromResult<TechnicianInventory?>(inventories.SingleOrDefault(i => i.TechnicianId == (TechnicianId)a![0]!)),
            nameof(ITechnicianInventoryRepository.FindStockItemsByTechnicianIdAsync) => Task.FromResult<IEnumerable<ComponentStock>>(
                inventories.SingleOrDefault(i => i.TechnicianId == (TechnicianId)a![0]!)?.StockItems.ToList() ?? []),
            nameof(ITechnicianInventoryRepository.AddAsync) => Task.Run(() => inventories.Add((TechnicianInventory)a![0]!)),
            _ => throw new InvalidOperationException("Unexpected inventory call " + m.Name)
        });
        // No component catalogue in this fixture: names fall back and unknown components are not found.
        var components = AssignmentHttpFixture.Proxy<IComponentRepository>((m, a) => m.Name switch
        {
            nameof(IComponentRepository.FindByIdsAsync) => Task.FromResult<IEnumerable<Component>>([]),
            nameof(IComponentRepository.FindByIdAsync) => Task.FromResult<Component?>(null),
            _ => throw new InvalidOperationException("Unexpected component call " + m.Name)
        });
        var componentTypes = AssignmentHttpFixture.Proxy<IComponentTypeRepository>((m, a) => m.Name == nameof(IComponentTypeRepository.FindByIdAsync)
            ? Task.FromResult<ComponentType?>(null) : throw new InvalidOperationException("Unexpected component type call " + m.Name));
        var unit = AssignmentHttpFixture.Proxy<IUnitOfWork>((m, a) => { Saves++; return Task.CompletedTask; });
        var mediator = AssignmentHttpFixture.Proxy<IMediator>((m, a) => { Events++; return Task.CompletedTask; });
        var actors = new OwnerActorDoubles();
        server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            actors.AddTo(services);
            services.AddSingleton<ITechnicianInventoryCommandService>(new TechnicianInventoryCommandService(inventoryRepository, components, componentTypes, unit, mediator));
            services.AddSingleton<ITechnicianInventoryQueryService>(new TechnicianInventoryQueryService(inventoryRepository, components));
            services.AddControllers(o => o.Conventions.Add(new Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Configuration.KebabCaseRouteNamingConvention()))
                .AddApplicationPart(typeof(TechnicianInventoriesController).Assembly).ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlyInventory()));
        }).Configure(app =>
        {
            app.UseRouting(); app.UseMiddleware<RequestAuthorizationMiddleware>(); app.UseEndpoints(e => e.MapControllers());
        })); client = server.CreateClient();
    }

    internal static string Route(string technician) => "/api/v1/technicians/" + technician + "/inventory";

    internal int Quantity() => ownerInventory.StockItems.Single(s => s.ComponentId.Value == Component).QuantityAvailable;

    internal string Snapshot() => JsonSerializer.Serialize(new
    {
        Inventories = inventories.Count,
        Stock = inventories.SelectMany(i => i.StockItems.Select(s => i.TechnicianId.Value + ":" + s.ComponentId.Value + ":" + s.QuantityAvailable)).OrderBy(v => v).ToArray()
    });

    internal void AssertUnchanged(string before)
    {
        Assert.Equal(before, Snapshot()); Assert.Equal(0, Saves); Assert.Equal(0, Events);
    }

    // Every action targets the owner's inventory; the token decides who is asking.
    internal Task<HttpResponseMessage> Act(string? token, string action)
    {
        var root = Route(OwnerActorDoubles.TechOwner);
        return action switch
        {
            "get" => Send(HttpMethod.Get, root, token),
            "stock-items" => Send(HttpMethod.Get, root + "/stock-items", token),
            "create" => Send(HttpMethod.Post, root, token),
            "add-stock" => Send(HttpMethod.Post, root + "/stock-items", token, new { componentId = "cmpt-new-m324", componentTypeId = "ctype-new-m324", quantity = 1, alertThreshold = 0 }),
            "update" => Send(HttpMethod.Put, root + "/" + Component, token, new { newQuantity = 99, newAlertThreshold = 0 }),
            "remove" => Send(HttpMethod.Delete, root + "/" + Component, token),
            "increase" => Send(HttpMethod.Patch, root + "/" + Component + "/increase", token, new { amount = 3 }),
            "decrease" => Send(HttpMethod.Patch, root + "/" + Component + "/decrease", token, new { amount = 2 }),
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

    private sealed class OnlyInventory : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        { foreach (var c in feature.Controllers.ToArray()) if (c.AsType() != typeof(TechnicianInventoriesController)) feature.Controllers.Remove(c); }
    }
}
