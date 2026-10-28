using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hampcoders.Electrolink.API.Assets.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Assets.Interfaces.REST.Controllers;
using Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Components;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Tokens.JWT.Configuration;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Tokens.JWT.Services;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.Assets;

public sealed class DeviceRegistrationHttpTests
{
    [Fact]
    public async Task Active_technician_registers_own_stock_with_hash_and_existing_public_DTO()
    {
        using var h = new RegistrationHttpFixture(AccessRole.User);
        using var response = await h.Send(); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(8, dto.EnumerateObject().Count());
        Assert.Equal(0, dto.GetProperty("status").GetInt32()); Assert.DoesNotContain("key", dto.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var device = Assert.Single(h.Devices); Assert.Equal(EDeviceStatus.InStock, device.Status);
        Assert.True(device.ApiKeyHash.Verify(RegistrationHttpFixture.Key)); Assert.NotEqual(RegistrationHttpFixture.Key, device.ApiKeyHash.Value);
        Assert.Equal("tech-synthetic-m316", device.StockTechnicianId!.Value);
        Assert.Null(device.AssignedPropertyId); Assert.Null(device.InstallationRequestId); Assert.Null(device.InstalledAt);
        Assert.Equal(1, h.Saves); Assert.Equal(1, h.Events); Assert.NotNull(response.Headers.Location);
    }
    [Theory]
    [InlineData("anonymous", 401)]
    [InlineData("invalid", 401)]
    [InlineData("edge", 401)]
    [InlineData("client", 403)]
    [InlineData("admin", 403)]
    [InlineData("superadmin", 403)]
    [InlineData("maker", 403)]
    [InlineData("field-manager", 403)]
    [InlineData("suspended-technician", 403)]
    [InlineData("inactive-profile", 403)]
    [InlineData("no-profile", 403)]
    [InlineData("wrong-subject", 403)]
    [InlineData("missing-inventory", 403)]
    public async Task Ineligible_actor_cannot_register_or_publish(string mode, int status)
    {
        var role = mode switch { "admin" => AccessRole.Admin, "superadmin" => AccessRole.SuperAdmin,
            "maker" => AccessRole.Maker, "field-manager" => AccessRole.FieldManager, _ => AccessRole.User };
        using var h = new RegistrationHttpFixture(role, mode);
        using var response = await h.Send(); Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Empty(h.Devices); Assert.Equal(0, h.Saves); Assert.Equal(0, h.Events); Assert.Equal(0, h.RepositoryCalls);
    }
    [Theory]
    [InlineData("serial")]
    [InlineData("key")]
    [InlineData("firmware")]
    [InlineData("long-serial")]
    [InlineData("long-firmware")]
    public async Task Invalid_input_returns_400_before_persistence(string field)
    {
        using var h = new RegistrationHttpFixture(AccessRole.User);
        using var response = await h.Send(body: new { serialNumber = field == "serial" ? " " : field == "long-serial" ? new string('x', 101) : "SYNTHETIC-M316",
            plainTextApiKey = field == "key" ? " " : RegistrationHttpFixture.Key,
            firmwareVersion = field == "firmware" ? " " : field == "long-firmware" ? new string('x', 51) : "synthetic" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Empty(h.Devices); Assert.Equal(0, h.Saves); Assert.Equal(0, h.Events);
        Assert.DoesNotContain(RegistrationHttpFixture.Key, await response.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task Duplicate_serial_returns_409_without_second_save_or_event()
    {
        using var h = new RegistrationHttpFixture(AccessRole.User);
        using var first = await h.Send(); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var repeat = await h.Send(); Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        Assert.Single(h.Devices); Assert.Equal(1, h.Saves); Assert.Equal(1, h.Events);
        Assert.Equal("{\"message\":\"Device serial number already exists.\"}", await repeat.Content.ReadAsStringAsync());
    }
    [Theory]
    [InlineData(AccessRole.User, 201)]
    [InlineData(AccessRole.Admin, 403)]
    public async Task Legacy_post_alias_enforces_identical_policy(AccessRole role, int status)
    {
        using var h = new RegistrationHttpFixture(role);
        using var response = await h.Send(path: "/api/v1/io-t-devices"); Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal(status == 201 ? 1 : 0, h.Saves);
    }
    [Fact]
    public async Task Own_stock_counts_pages_and_legacy_lists_exclude_foreign_and_unowned_devices()
    {
        using var h = new RegistrationHttpFixture(AccessRole.User);
        h.Seed("tech-synthetic-m316", "SYNTHETIC-OWN-1"); h.Seed("tech-synthetic-m316", "SYNTHETIC-OWN-2", assigned: true);
        h.Seed("tech-synthetic-m316", "SYNTHETIC-OWN-3"); h.Seed("tech-foreign", "SYNTHETIC-FOREIGN"); h.Seed(null, "SYNTHETIC-LEGACY");
        using var page = await h.Get("/api/v1/iot-devices/my-stock?pageSize=2&technicianId=tech-foreign");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode); var dto = await page.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, dto.GetProperty("totalCount").GetInt32()); Assert.Equal(2, dto.GetProperty("availableCount").GetInt32());
        Assert.Equal(2, dto.GetProperty("items").GetArrayLength()); Assert.True(dto.GetProperty("hasMore").GetBoolean());
        Assert.All(dto.GetProperty("items").EnumerateArray(), item => Assert.Equal(4, item.EnumerateObject().Count()));
        Assert.DoesNotContain("key", dto.GetRawText(), StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("SYNTHETIC-FOREIGN", dto.GetRawText());
        using var next = await h.Get("/api/v1/iot-devices/my-stock?page=2&pageSize=2");
        var nextDto = await next.Content.ReadFromJsonAsync<JsonElement>(); Assert.Single(nextDto.GetProperty("items").EnumerateArray()); Assert.False(nextDto.GetProperty("hasMore").GetBoolean());
        using var legacy = await h.Get("/api/v1/io-t-devices"); Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        Assert.Equal(3, (await legacy.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
        using var available = await h.Get("/api/v1/io-t-devices/by-status/InStock"); Assert.Equal(HttpStatusCode.OK, available.StatusCode);
        Assert.Equal(2, (await available.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
        Assert.Equal(0, h.Saves); Assert.Equal(0, h.Events);
    }
    [Fact]
    public async Task Created_location_reads_only_the_registering_technicians_stock()
    {
        using var h = new RegistrationHttpFixture(AccessRole.User);
        using var created = await h.Send(); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Contains("/api/v1/iot-devices/", created.Headers.Location!.ToString());
        using var own = await h.Get(created.Headers.Location.ToString()); Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(8, (await own.Content.ReadFromJsonAsync<JsonElement>()).EnumerateObject().Count());
        h.Seed("tech-foreign", "SYNTHETIC-FOREIGN-READ");
        using var foreign = await h.Get("/api/v1/iot-devices/" + h.Devices[1].Id.Value); Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var missing = await h.Get("/api/v1/iot-devices/dev-synthetic-missing"); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(1, h.Saves); Assert.Equal(1, h.Events);
    }
    [Fact]
    public async Task Legacy_active_count_checks_the_actor_and_never_counts_foreign_inventory()
    {
        using var h = new RegistrationHttpFixture(AccessRole.User);
        h.Seed("tech-synthetic-m316", "SYNTHETIC-OWN-ACTIVE", assigned: true);
        h.Devices[0].RecordInstallation(TechnicianId.From("tech-synthetic-m316"), PropertyId.From("prop-synthetic"), "synthetic", DateTime.UtcNow);
        h.Seed("tech-foreign", "SYNTHETIC-FOREIGN-ACTIVE", assigned: true);
        h.Devices[1].RecordInstallation(TechnicianId.From("tech-foreign"), PropertyId.From("prop-synthetic"), "synthetic", DateTime.UtcNow);
        using var own = await h.Get("/api/v1/io-t-devices/active-count/tech-synthetic-m316"); Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(1, (await own.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("count").GetInt32());
        using var foreign = await h.Get("/api/v1/io-t-devices/active-count/tech-foreign"); Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(0, h.Saves); Assert.Equal(0, h.Events);
    }
    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=2147483647&pageSize=100")]
    public async Task Invalid_stock_page_returns_400(string query)
    {
        using var h = new RegistrationHttpFixture(AccessRole.User);
        using var response = await h.Get("/api/v1/iot-devices/my-stock?" + query); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, h.Saves); Assert.Equal(0, h.Events);
    }
    [Theory]
    [InlineData("anonymous", 401)]
    [InlineData("edge", 401)]
    [InlineData("client", 403)]
    [InlineData("admin", 403)]
    [InlineData("suspended-technician", 403)]
    public async Task Stock_query_requires_active_technician(string mode, int status)
    {
        using var h = new RegistrationHttpFixture(mode == "admin" ? AccessRole.Admin : AccessRole.User, mode);
        using var response = await h.Get("/api/v1/iot-devices/my-stock"); Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal(0, h.RepositoryCalls);
    }

}

// Real JWT/middleware/action/command/domain; synthetic repository, IAM lookup, UoW and event observer.
internal sealed class RegistrationHttpFixture : IDisposable
{
    internal const string Key = "synthetic-m316-key-only";
    private const string Secret = "synthetic-m316-signing-key-only-at-least-32-bytes";
    internal List<IoTDevice> Devices { get; } = [];
    internal int Saves, Events, RepositoryCalls;
    private readonly TestServer server;
    private readonly HttpClient client;
    private readonly string token;
    private readonly string mode;
    internal RegistrationHttpFixture(AccessRole role, string mode = "valid")
    {
        this.mode = mode;
        var user = User.Create(Email.From("m316@example.invalid"), HashedPassword.FromHash("synthetic-unused"), role);
        var tokens = new TokenService(Options.Create(new TokenSettings { Secret = Secret }), NullLogger<TokenService>.Instance);
        token = tokens.GenerateToken(user);
        if (mode.StartsWith("suspended", StringComparison.Ordinal)) user.Suspend("Synthetic");
        var devices = AssignmentHttpFixture.Proxy<IIoTDeviceRepository>((m, a) =>
        {
            RepositoryCalls++;
            if (m.Name == nameof(IIoTDeviceRepository.FindByIdAsync)) return Task.FromResult(Devices.SingleOrDefault(d => d.Id == (IoTDeviceId)a![0]!));
            if (m.Name == nameof(IIoTDeviceRepository.FindBySerialNumberAsync)) return Task.FromResult(Devices.SingleOrDefault(d => d.SerialNumber == (SerialNumber)a![0]!));
            if (m.Name == nameof(IIoTDeviceRepository.AddAsync)) { Devices.Add((IoTDevice)a![0]!); return Task.CompletedTask; }
            var own = Devices.Where(d => d.StockTechnicianId == (TechnicianId)a![0]!).OrderBy(d => d.Id.Value).ToArray();
            if (m.Name == nameof(IIoTDeviceRepository.GetTechnicianStockPageAsync))
            {
                var page = (int)a![1]!; var size = (int)a[2]!;
                return Task.FromResult(new Hampcoders.Electrolink.API.Assets.Domain.Model.Queries.TechnicianDeviceStockPage(own.Skip((page - 1) * size).Take(size).ToArray(),
                    own.Length, own.Count(d => d.Status == EDeviceStatus.InStock && d.AssignedPropertyId == null && d.InstallationRequestId == null)));
            }
            if (m.Name == nameof(IIoTDeviceRepository.CountTechnicianActiveDevicesAsync)) return Task.FromResult(own.Count(d => d.Status is EDeviceStatus.Installed or EDeviceStatus.Maintenance));
            if (m.Name == nameof(IIoTDeviceRepository.FindTechnicianStockAsync)) return Task.FromResult<IEnumerable<IoTDevice>>(own.Where(d => a![1] == null || d.Status == (EDeviceStatus)a[1]!));
            throw new InvalidOperationException("Unexpected device repository call");
        });
        var properties = AssignmentHttpFixture.Proxy<IPropertyRepository>((m, a) => throw new InvalidOperationException("Registration must not access properties"));
        var profiles = AssignmentHttpFixture.Proxy<IProfilesContextFacade>((m, a) => m.Name switch
        {
            nameof(IProfilesContextFacade.GetProfileClaimsAsync) => Task.FromResult<(string ProfileId, string ProfileStatus, string? BusinessRole, string? RoleSubjectId, string? SubscriptionTier)?>(
                mode == "no-profile" ? null : ("prof-synthetic", mode == "inactive-profile" ? "Deactivated" : "Active",
                    role == AccessRole.User && mode != "client" ? "Technician" : "HomeOwner", "tech-synthetic-m316", null)),
            nameof(IProfilesContextFacade.GetTechnicianIdByUserIdAsync) => Task.FromResult<string?>(mode == "wrong-subject" ? "tech-other" : "tech-synthetic-m316"),
            _ => throw new InvalidOperationException("Unexpected profile call")
        });
        var unit = AssignmentHttpFixture.Proxy<IUnitOfWork>((m, a) => { Saves++; return Task.CompletedTask; });
        var publisher = AssignmentHttpFixture.Proxy<IPublisher>((m, a) => { Events++; return Task.CompletedTask; });
        server = new TestServer(new WebHostBuilder().ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["EdgeApi:ApiKey"] = "synthetic-edge" }))
            .ConfigureServices(s =>
            {
                s.AddSingleton<ITokenService>(tokens);
                s.AddScoped<TechnicianDeviceStockService>();
                s.AddSingleton(profiles);
                s.AddSingleton(devices);
                s.AddSingleton(AssignmentHttpFixture.Proxy<ITechnicianInventoryRepository>((m, a) => Task.FromResult<TechnicianInventory?>(
                    mode == "missing-inventory" ? null : TechnicianInventory.Create(TechnicianId.From("tech-synthetic-m316")))));
                s.AddSingleton(AssignmentHttpFixture.Proxy<IUserQueryService>((m, a) => Task.FromResult<User?>(user)));
                s.AddSingleton<IIoTDeviceCommandService>(new IoTDeviceCommandService(devices, properties, unit, publisher, profiles));
                s.AddSingleton(AssignmentHttpFixture.Proxy<IIoTDeviceQueryService>((m, a) => throw new InvalidOperationException("Unexpected query")));
                s.AddControllers(o => o.Conventions.Add(new Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Configuration.KebabCaseRouteNamingConvention()))
                    .AddApplicationPart(typeof(IoTDevicesController).Assembly).ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlyRegistrationController()));
                s.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => o.TokenValidationParameters = new()
                { ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(Secret)), ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = true });
                s.AddAuthorization();
            }).Configure(app => { app.UseRouting(); app.UseAuthentication(); app.UseMiddleware<RequestAuthorizationMiddleware>(); app.UseAuthorization(); app.UseEndpoints(e => e.MapControllers()); }));
        client = server.CreateClient();
    }
    internal Task<HttpResponseMessage> Send(object? body = null, string path = "/api/v1/iot-devices") => SendRequest(HttpMethod.Post, path, body);
    internal Task<HttpResponseMessage> Get(string path) => SendRequest(HttpMethod.Get, path, null);
    internal void Seed(string? technician, string serial, bool assigned = false)
    {
        var hash = ApiKeyHash.CreateFromPlainText(Key);
        var device = technician == null ? IoTDevice.Register(SerialNumber.From(serial), hash, "synthetic")
            : IoTDevice.RegisterForTechnician(SerialNumber.From(serial), hash, "synthetic", TechnicianId.From(technician));
        if (assigned) device.AssignToProperty(PropertyId.From("prop-synthetic"), InstallationRequestId.From("req-synthetic"));
        device.ClearDomainEvents(); Devices.Add(device);
    }
    private async Task<HttpResponseMessage> SendRequest(HttpMethod method, string path, object? body)
    {
        using var request = new HttpRequestMessage(method, path);
        if (mode is not ("anonymous" or "edge")) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + (mode == "invalid" ? "invalid.synthetic" : token));
        if (mode == "edge") request.Headers.Add("X-Api-Key", "synthetic-edge");
        if (method == HttpMethod.Post) request.Content = JsonContent.Create(body ?? new { serialNumber = "SYNTHETIC-M316", plainTextApiKey = Key, firmwareVersion = "synthetic" });
        return await client.SendAsync(request);
    }
    public void Dispose() { client.Dispose(); server.Dispose(); }
    private sealed class OnlyRegistrationController : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        { foreach (var c in feature.Controllers.ToArray()) if (c.AsType() != typeof(IoTDevicesController)) feature.Controllers.Remove(c); }
    }
}
