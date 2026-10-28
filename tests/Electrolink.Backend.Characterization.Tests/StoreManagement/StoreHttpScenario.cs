using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Queries;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Components;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Tokens.JWT.Configuration;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Tokens.JWT.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;
using Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Configuration;
using Hampcoders.Electrolink.API.StoreManagement.Interfaces.REST;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

internal sealed class StoreHttpScenario : IDemoHttpScenario
{
    private const string Secret = "synthetic-m31-secret-key-only-for-tests-at-least-32-bytes";
    private const string EdgeKey = "synthetic-m31-edge-key";
    private readonly string activeEdgeKey;
    public User Owner { get; } = User.Create(Email.From("m31-owner@example.invalid"), HashedPassword.FromHash("synthetic"), AccessRole.Admin);
    public User Other { get; } = User.Create(Email.From("m31-other@example.invalid"), HashedPassword.FromHash("synthetic"), AccessRole.Admin);
    internal MemoryStoreRepository Repository { get; } = new();
    private readonly TokenService tokens;
    private readonly TestServer server;
    private readonly HttpClient client;
    internal StoreHttpScenario(EquipmentTelemetryScenario? telemetry = null, PostgresScenarioSettings? postgres = null)
    {
        if (postgres?.Owner != null) Owner = postgres.Owner;
        if (postgres?.Other != null) Other = postgres.Other;
        var secret = postgres?.JwtSecret ?? Secret;
        activeEdgeKey = postgres?.EdgeKey ?? EdgeKey;
        tokens = new TokenService(Options.Create(new TokenSettings { Secret = secret }), NullLogger<TokenService>.Instance);
        server = new TestServer(new WebHostBuilder()
            .ConfigureAppConfiguration((_, c) => { c.Sources.Clear(); c.AddInMemoryCollection(new Dictionary<string, string?> { ["EdgeApi:ApiKey"] = activeEdgeKey,
                    ["EdgeApi:EquipmentReadings:AllowedDeviceIds:0"] = telemetry?.MachineScope == true ? telemetry.Device.SerialNumber.Value : null,
                    ["EdgeApi:EquipmentReadings:AllowedDeviceIds:1"] = telemetry?.MachineScope == true ? "dev-m32-scope-without-binding" : null }); })
            .ConfigureServices(services =>
            {
                // PostgreSQL demo keeps both production EF repositories; ordinary tests use doubles.
                services.AddStoreManagement(postgres?.ConnectionString ?? "Host=127.0.0.1;Port=1;Database=synthetic_m31;Username=synthetic");
                if (postgres == null)
                {
                    services.RemoveAll<IStoreHierarchyRepository>();
                    services.AddSingleton<IStoreHierarchyRepository>(Repository);
                    if (telemetry != null) telemetry.Alerts.Hierarchy = Repository;
                }
                telemetry?.Register(services, Owner.Id.Value, realPersistence: postgres != null);
                services.AddSingleton<ITokenService>(tokens);
                services.AddSingleton<IUserQueryService>(new Users(Owner, Other));
                services.AddControllers(options => options.Conventions.Add(
                    new Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Configuration.KebabCaseRouteNamingConvention())).AddApplicationPart(typeof(StoresController).Assembly)
                    .ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlyStoreControllers(telemetry != null)));
                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secret)),
                        ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = true, ClockSkew = TimeSpan.Zero
                    });
                services.AddAuthorization();
            })
            .Configure(app =>
            {
                app.UseRouting(); app.UseAuthentication();
                app.UseMiddleware<RequestAuthorizationMiddleware>(); app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));
        client = server.CreateClient();
    }
    public async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null,
        bool otherUser = false, bool anonymous = false, bool edgeKey = false)
    {
        using var request = new HttpRequestMessage(method, path);
        if (!anonymous) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GenerateToken(otherUser ? Other : Owner));
        if (edgeKey) request.Headers.Add("X-Api-Key", activeEdgeKey);
        if (body != null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    public async Task<JsonElement> Create(string path, object body)
    {
        var response = await Send(HttpMethod.Post, path, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var value = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotNull(response.Headers.Location);
        Assert.EndsWith(value.GetProperty("id").GetString()!, response.Headers.Location!.ToString());
        return value;
    }
    public void Dispose() { client.Dispose(); server.Dispose(); }
    private sealed class OnlyStoreControllers(bool telemetry) : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in feature.Controllers.ToArray())
                if (controller.AsType() != typeof(StoresController) && (!telemetry || (controller.AsType() != typeof(Hampcoders.Electrolink.API.AlertManagement.Interfaces.EquipmentAlertsController) && controller.AsType() != typeof(Hampcoders.Electrolink.API.AlertManagement.Interfaces.StoreAlertsController) && controller.AsType() != typeof(EquipmentDeviceController)
                    && controller.AsType() != typeof(Hampcoders.Electrolink.API.Processing.Interfaces.REST.EdgeIngestionController)))) feature.Controllers.Remove(controller);
        }
    }
    private sealed class Users(params User[] users) : IUserQueryService
    {
        public Task<User?> Handle(GetUserByIdQuery query) => Task.FromResult(users.SingleOrDefault(u => u.Id.Value == query.UserId));
        public Task<IEnumerable<User>> Handle(GetAllUsersQuery query) => throw new InvalidOperationException("Unexpected user listing");
        public Task<User?> Handle(GetUserByEmailQuery query) => throw new InvalidOperationException("Unexpected email lookup");
    }
}

internal sealed record PostgresScenarioSettings(string ConnectionString, string JwtSecret, string EdgeKey)
{
    internal User? Owner { get; init; }
    internal User? Other { get; init; }
}

internal sealed class MemoryStoreRepository : IStoreHierarchyRepository
{
    private readonly List<Store> stores = new();
    private readonly List<StoreArea> areas = new();
    private readonly List<ElectricalEquipment> equipment = new();
    private readonly List<object> pending = new();
    internal int ConfirmedSaves { get; private set; }
    public Task<Store?> FindStoreAsync(UserId actor, string storeId, CancellationToken ct) =>
        Task.FromResult(stores.SingleOrDefault(s => s.Id == storeId && s.OwnerUserId == actor));
    private bool Owns(UserId actor, string storeId) => stores.Any(s => s.Id == storeId && s.OwnerUserId == actor);
    public Task<StoreArea?> FindAreaAsync(UserId actor, string storeId, string areaId, CancellationToken ct) =>
        Task.FromResult(Owns(actor, storeId) ? areas.SingleOrDefault(a => a.Id == areaId && a.StoreId == storeId) : null);
    public async Task<ElectricalEquipment?> FindEquipmentAsync(UserId actor, string storeId, string areaId, string equipmentId, CancellationToken ct) =>
        await FindAreaAsync(actor, storeId, areaId, ct) != null ? equipment.SingleOrDefault(e => e.Id == equipmentId && e.AreaId == areaId) : null;
    public Task<ElectricalEquipment?> FindEquipmentInStoreAsync(UserId actor, string storeId, string equipmentId, CancellationToken ct) =>
        Task.FromResult(Owns(actor, storeId) ? equipment.SingleOrDefault(e => e.Id == equipmentId && areas.Any(a => a.Id == e.AreaId && a.StoreId == storeId)) : null);
    public Task<List<Store>> ListStoresAsync(UserId actor, int skip, int take, CancellationToken ct) =>
        Task.FromResult(stores.Where(s => s.OwnerUserId == actor).OrderBy(s => s.Id, StringComparer.Ordinal).Skip(skip).Take(take).ToList());
    public Task<List<StoreArea>> ListAreasAsync(UserId actor, string storeId, int skip, int take, CancellationToken ct) =>
        Task.FromResult(areas.Where(a => a.StoreId == storeId && Owns(actor, storeId)).OrderBy(a => a.Id, StringComparer.Ordinal).Skip(skip).Take(take).ToList());
    public async Task<List<ElectricalEquipment>> ListEquipmentAsync(UserId actor, string storeId, string areaId, int skip, int take, CancellationToken ct) =>
        await FindAreaAsync(actor, storeId, areaId, ct) != null ? equipment.Where(e => e.AreaId == areaId).OrderBy(e => e.Id, StringComparer.Ordinal).Skip(skip).Take(take).ToList() : new();
    public void Add(Store store) => pending.Add(store);
    public void Add(StoreArea area) => pending.Add(area);
    public void Add(ElectricalEquipment item) => pending.Add(item);
    public Task SaveAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        foreach (var item in pending)
        {
            // Copies only at confirmation; these entities have no update operations in M3.1.
            switch (item)
            {
                case Store s: stores.Add(CopyId(Store.Register(s.OwnerUserId, s.Name, s.Address), s.Id)); break;
                case StoreArea a: areas.Add(CopyId(StoreArea.Register(a.StoreId, a.Name), a.Id)); break;
                case ElectricalEquipment e: equipment.Add(CopyId(ElectricalEquipment.Register(e.AreaId, e.Name), e.Id)); break;
            }
        }
        pending.Clear(); ConfirmedSaves++;
        return Task.CompletedTask;
    }
    private static T CopyId<T>(T copy, string id) { typeof(T).GetProperty("Id")!.SetValue(copy, id); return copy; }
}
