using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Electrolink.Backend.Characterization.Tests.StoreManagement;
using Hampcoders.Electrolink.API.Assets.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Assets.Interfaces.REST.Controllers;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.Assets;

public sealed class DeviceAssignmentHttpTests
{
    [Fact]
    public async Task Assigned_technician_assigns_explicit_free_device_with_existing_DTO_and_event()
    {
        using var h = new AssignmentHttpFixture("valid");
        using var response = await h.Send(); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(EDeviceStatus.Assigned, h.Device.Status);
        Assert.NotNull(h.Device.AssignedPropertyId); Assert.Equal("req-synthetic-m314", h.Device.InstallationRequestId!.Value);
        Assert.Null(h.Device.InstalledAt); Assert.Null(h.Device.InstalledByTechnicianId);
        Assert.Equal(1, h.Saves); Assert.Equal(1, h.Events);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(8, body.EnumerateObject().Count());
        Assert.Equal((int)EDeviceStatus.Assigned, body.GetProperty("status").GetInt32());
        Assert.DoesNotContain("key", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }
    [Theory]
    [InlineData("foreign", 404)]
    [InlineData("anonymous", 401)]
    [InlineData("edge", 401)]
    [InlineData("occupied", 409)]
    [InlineData("wrong-property", 404)]
    [InlineData("inactive-profile", 404)]
    [InlineData("unassigned-request", 404)]
    [InlineData("foreign-stock", 404)]
    [InlineData("unowned-stock", 404)]
    public async Task Missing_relation_or_busy_stock_never_assigns_saves_or_publishes(string condition, int status)
    {
        using var h = new AssignmentHttpFixture(condition); var before = JsonSerializer.Serialize(h.Device);
        using var response = await h.Send(); Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal(before, JsonSerializer.Serialize(h.Device)); Assert.Equal(0, h.Saves); Assert.Equal(0, h.Events);
    }
}
// Synthetic identity/persistence boundary; JWT, Profiles, Planning and EF are verified separately in the PostgreSQL mode.
internal sealed class AssignmentHttpFixture : IDisposable
{
    internal const string Technician = "tech-synthetic-m313";
    internal DateTime At { get; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    internal IoTDevice Device { get; }
    internal int Saves { get; private set; }
    internal int Events { get; private set; }
    private readonly string propertyId;
    private readonly string condition;
    private readonly TestServer server;
    private readonly HttpClient client;
    internal AssignmentHttpFixture(string condition)
    {
        this.condition = condition;
        var actor = User.Create(Email.From("m313-tech@example.invalid"), HashedPassword.FromHash("synthetic"), AccessRole.Admin);
        var other = User.Create(Email.From("m313-other@example.invalid"), HashedPassword.FromHash("synthetic"), AccessRole.Admin);
        var property = Property.Create(ClientIdentity.FromHomeowner("ho-synthetic-m313"), Address.Create("Synthetic", "1", "Synthetic", "Synthetic", "Synthetic", "00000"), Geolocation.Create(0, 0, null, "SYNTHETIC"));
        propertyId = property.Id.Value;
        Device = condition == "unowned-stock"
            ? IoTDevice.Register(SerialNumber.From("SYNTHETIC-M314"), ApiKeyHash.CreateFromPlainText(Guid.NewGuid().ToString()), "synthetic-before")
            : IoTDevice.RegisterForTechnician(SerialNumber.From("SYNTHETIC-M314"), ApiKeyHash.CreateFromPlainText(Guid.NewGuid().ToString()), "synthetic-before",
                TechnicianId.From(condition == "foreign-stock" ? "tech-foreign-stock" : Technician));
        if (condition == "occupied") Device.AssignToProperty(property.Id, InstallationRequestId.From("req-other-synthetic"));
        Device.ClearDomainEvents();
        var devices = Proxy<IIoTDeviceRepository>((m, a) => m.Name == nameof(IIoTDeviceRepository.FindByIdAsync) ? Task.FromResult<IoTDevice?>(Device) : throw new InvalidOperationException("Unexpected device call"));
        var properties = Proxy<IPropertyRepository>((m, a) => m.Name == nameof(IPropertyRepository.FindByIdAsync) ? Task.FromResult<Property?>((PropertyId)a![0]! == property.Id ? property : null) : throw new InvalidOperationException("Unexpected property call"));
        var profiles = Proxy<IProfilesContextFacade>((m, a) => m.Name switch
        {
            nameof(IProfilesContextFacade.GetProfileClaimsAsync) => Task.FromResult<(string ProfileId, string ProfileStatus, string? BusinessRole, string? RoleSubjectId, string? SubscriptionTier)?>(
                ("prof-synthetic-m313", condition == "inactive-profile" ? "Deactivated" : "Active", "Technician", (string)a![0]! == actor.Id.Value ? Technician : "tech-other-m313", null)),
            nameof(IProfilesContextFacade.GetTechnicianIdByUserIdAsync) => Task.FromResult<string?>((string)a![0]! == actor.Id.Value ? Technician : "tech-other-m313"),
            _ => throw new InvalidOperationException("Unexpected profile call")
        });
        var unit = Proxy<IUnitOfWork>((m, a) => { Saves++; return Task.CompletedTask; });
        var publisher = Proxy<IPublisher>((m, a) => { Events++; return Task.CompletedTask; });
        server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton<Hampcoders.Electrolink.API.Planning.Interfaces.ACL.IInstallationAssignmentReader>(
                Proxy<Hampcoders.Electrolink.API.Planning.Interfaces.ACL.IInstallationAssignmentReader>((m, a) => Task.FromResult(condition is not ("unassigned-request" or "foreign"))));
            services.AddScoped<DeviceAssignmentService>();
            services.AddSingleton(devices); services.AddSingleton(properties); services.AddSingleton(profiles);
            services.AddSingleton<IIoTDeviceCommandService>(new IoTDeviceCommandService(devices, properties, unit, publisher, profiles));
            services.AddSingleton<IIoTDeviceQueryService>(new Hampcoders.Electrolink.API.Assets.Application.Internal.QueryServices.IoTDeviceQueryService(devices, properties, profiles));
            services.AddControllers(o => o.Conventions.Add(new Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Configuration.KebabCaseRouteNamingConvention()))
                .AddApplicationPart(typeof(IoTDevicesController).Assembly).ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlyDeviceController()));
        }).Configure(app =>
        {
            app.UseRouting(); app.Use(async (context, next) =>
            {
                if (context.Request.Headers.Authorization == "Bearer synthetic-authorized") context.Items["User"] = actor;
                if (context.Request.Headers.Authorization == "Bearer synthetic-foreign") context.Items["User"] = other;
                await next();
            }); app.UseEndpoints(e => e.MapControllers());
        })); client = server.CreateClient();
    }
    internal Task<HttpResponseMessage> Send()
    {
        var property = condition == "wrong-property" ? "prop-other-synthetic-m314" : propertyId;
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/io-t-devices/" + Device.Id.Value + "/assign/" + property);
        if (condition is not ("anonymous" or "edge")) request.Headers.TryAddWithoutValidation("Authorization", condition == "foreign" ? "Bearer synthetic-foreign" : "Bearer synthetic-authorized");
        if (condition == "edge") request.Headers.Add("X-Api-Key", "synthetic-edge-only");
        request.Content = JsonContent.Create(new { installationRequestId = "req-synthetic-m314" });
        return client.SendAsync(request);
    }
    internal static T Proxy<T>(Func<MethodInfo, object?[]?, object?> call) where T : class
    { var p = DispatchProxy.Create<T, DeviceAuthorityDouble>(); ((DeviceAuthorityDouble)(object)p).Call = call; return p; }
    public void Dispose() { client.Dispose(); server.Dispose(); }
    private sealed class OnlyDeviceController : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        { foreach (var c in feature.Controllers.ToArray()) if (c.AsType() != typeof(IoTDevicesController)) feature.Controllers.Remove(c); }
    }
}
