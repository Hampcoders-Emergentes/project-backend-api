using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Queries;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Components;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Infrastructure.Interfaces.ASP.Configuration.Extensions;
using Hampcoders.Electrolink.API.Profiles.Infrastructure.Interfaces.ASP.Configuration.Extensions;
using Hampcoders.Electrolink.API.Assets.Infrastructure.Interfaces.ASP.Configuration.Extensions;
using Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Configuration;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Repositories;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces;
using Hampcoders.Electrolink.API.Processing.Domain.Repositories;
using Hampcoders.Electrolink.API.Processing.Domain.Services;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

// No identity/inventory dictionary. All tokens are obtained from the real authentication endpoint.
internal sealed class RealBackendScenario : IDemoHttpScenario
{
    private readonly TestServer server;
    private readonly HttpClient client;
    private readonly string edgeKey;
    private string? ownerToken;
    private string? otherToken;
    public User Owner { get; private set; } = null!;
    public User Other { get; private set; } = null!;
    internal RetirementEventWitness RetirementEvents { get; } = new();
    internal int AssetMutationEvents { get; private set; }
    internal int ForbiddenCalls { get; private set; }
    internal RealBackendScenario(string connection, string secret, string edgeKey, string serial, bool machineScope = true, bool includeRetirementHandler = false, bool includeRetirementEndpoint = false, bool observeAssetMutations = false, CompanyCompletionTrace? companyTrace = null, bool includeInstallationHandler = false)
    {
        this.edgeKey = edgeKey;
        // Call actual module registration extensions without building/running this WebApplicationBuilder.
        var registrations = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Production" });
        registrations.Configuration.Sources.Clear();
        registrations.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["TokenSettings:Secret"] = secret });
        var start = registrations.Services.Count;
        registrations.AddIamContextServices(); registrations.AddProfilesContextServices(); registrations.AddAssetsContextService();
        var moduleServices = registrations.Services.Skip(start).ToArray();
        server = new TestServer(new WebHostBuilder()
            .ConfigureAppConfiguration((_, c) => { c.Sources.Clear(); c.AddInMemoryCollection(new Dictionary<string, string?>
            { ["TokenSettings:Secret"] = secret, ["EdgeApi:ApiKey"] = edgeKey,
                ["EdgeApi:EquipmentReadings:AllowedDeviceIds:0"] = machineScope ? serial : null }); })
            .ConfigureServices(services =>
            {
                services.AddLogging(l => l.ClearProviders());
                foreach (var descriptor in moduleServices) services.Add(descriptor);
                services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connection, pg => pg.UseNetTopologySuite()));
                services.AddScoped<IUnitOfWork, UnitOfWork>();
                services.AddScoped<IOrphanedFileRepository, OrphanedFileRepository>();
                services.AddStoreManagement(connection);
                // Real MediatR 11, selected in-process handlers only. No broad assembly scan or subscription/provider handlers.
                services.AddScoped<IMediator>(sp => new Mediator(t => sp.GetRequiredService(t)));
                services.AddScoped<IPublisher>(sp => sp.GetRequiredService<IMediator>());
                services.AddScoped<ISender>(sp => sp.GetRequiredService<IMediator>());
                services.AddScoped<INotificationHandler<Hampcoders.Electrolink.API.IAM.Domain.Model.Events.UserRegisteredEvent>,
                    Hampcoders.Electrolink.API.Profiles.Application.Internal.EventHandlers.UserRegisteredEventHandler>();
                services.AddScoped<INotificationHandler<Hampcoders.Electrolink.API.IAM.Domain.Model.Events.UserSignedInEvent>,
                    Hampcoders.Electrolink.API.IAM.Application.Internal.EventHandlers.UserSignedInEventHandler>();
                services.AddScoped<INotificationHandler<Hampcoders.Electrolink.API.Profiles.Domain.Model.Events.ProfileCompletedEvent>,
                    Hampcoders.Electrolink.API.Assets.Application.Internal.EventHandlers.ProfileCompletedEventHandler>();
                if (includeInstallationHandler)
                    services.AddScoped<INotificationHandler<Hampcoders.Electrolink.API.Assets.Domain.Model.Events.DeviceInstalledEvent>,
                        Hampcoders.Electrolink.API.Assets.Application.Internal.EventHandlers.DeviceInstalledEventHandler>();
                if (includeRetirementHandler)
                    services.AddScoped<INotificationHandler<Hampcoders.Electrolink.API.Assets.Domain.Model.Events.DeviceDecommissionedEvent>,
                        Hampcoders.Electrolink.API.Assets.Application.Internal.EventHandlers.DeviceDecommissionedEventHandler>();
                if (includeRetirementEndpoint)
                    services.AddSingleton<INotificationHandler<Hampcoders.Electrolink.API.Assets.Domain.Model.Events.DeviceDecommissionedEvent>>(RetirementEvents);
                if (observeAssetMutations)
                {
                    var assembly = typeof(Hampcoders.Electrolink.API.Assets.Domain.Model.Events.DeviceDecommissionedEvent).Assembly;
                    foreach (var eventType in assembly.GetTypes().Where(t => t.Namespace == "Hampcoders.Electrolink.API.Assets.Domain.Model.Events"
                        && !t.IsAbstract && typeof(INotification).IsAssignableFrom(t)))
                    {
                        var observer = Activator.CreateInstance(typeof(AssetMutationObserver<>).MakeGenericType(eventType), (Action)(() => AssetMutationEvents++))!;
                        services.AddSingleton(typeof(INotificationHandler<>).MakeGenericType(eventType), observer);
                    }
                }
                if (companyTrace != null)
                    services.AddSingleton<INotificationHandler<Hampcoders.Electrolink.API.Profiles.Domain.Model.Events.ProfileCompletedEvent>>(companyTrace);
                services.AddSingleton(Block<IFileStorageService>());
                services.AddSingleton(Block<IDeviceReadingStreamCommandService>());
                services.AddSingleton(Block<IRelayCommandService>());
                services.AddSingleton(Block<IDeviceReadingStreamRepository>());
                services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(
                    builder => builder.PrimaryHandler = new ForbiddenHttpHandler(() => ForbiddenCalls++)));
                services.AddControllers(options =>
                {
                    options.Conventions.Add(new Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Configuration.KebabCaseRouteNamingConvention());
                    if (companyTrace != null) options.Filters.Add(companyTrace);
                })
                    .AddApplicationPart(typeof(Hampcoders.Electrolink.API.StoreManagement.Interfaces.REST.StoresController).Assembly)
                    .ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlyJourneyControllers(includeRetirementEndpoint)));
                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => options.TokenValidationParameters = new()
                { ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secret)),
                    ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = true, ClockSkew = TimeSpan.Zero });
                services.AddAuthorization();
            })
            .Configure(app => { app.UseRouting(); app.UseAuthentication(); app.UseMiddleware<RequestAuthorizationMiddleware>();
                app.UseAuthorization(); app.UseEndpoints(e => e.MapControllers()); }));
        client = server.CreateClient();
    }
    internal IServiceScope Scope() => server.Services.CreateScope();
    internal async Task RegisterAndComplete(string email, string password, string dni, bool other = false)
    {
        using var signUp = await Send(HttpMethod.Post, "/api/v1/authentication/sign-up",
            new { email, password, passwordConfirmation = password }, anonymous: true);
        Assert.Equal(HttpStatusCode.Created, signUp.StatusCode);
        var result = await signUp.Content.ReadFromJsonAsync<JsonElement>();
        SetToken(result.GetProperty("token").GetString()!, other);
        using var complete = await Send(HttpMethod.Post, "/api/v1/profiles/me/complete/homeowner", new
        {
            firstName = "Synthetic", lastName = other ? "Other" : "Owner", phoneNumber = other ? "+99900000002" : "+99900000001",
            dni, dateOfBirth = "1990-01-01", street = "Synthetic", number = "1", district = "Synthetic", city = "Synthetic", country = "Synthetic",
            postalCode = "00000", preferredContactTime = "Morning", smsNotifications = false, emailNotifications = true, pushNotifications = false
        }, otherUser: other);
        Assert.Equal(HttpStatusCode.Created, complete.StatusCode);
        await Login(email, password, other);
    }

    internal async Task RegisterAndCompleteTechnician(string email, string password, string dni, bool other = false)
    {
        using var signUp = await Send(HttpMethod.Post, "/api/v1/authentication/sign-up",
            new { email, password, passwordConfirmation = password }, anonymous: true);
        Assert.Equal(HttpStatusCode.Created, signUp.StatusCode);
        var result = await signUp.Content.ReadFromJsonAsync<JsonElement>(); SetToken(result.GetProperty("token").GetString()!, other);
        using var complete = await Send(HttpMethod.Post, "/api/v1/profiles/me/complete/technician", new
        {
            firstName = "Synthetic", lastName = "Technician", phoneNumber = "+999" + dni, dni, dateOfBirth = "1990-01-01",
            street = "Synthetic", number = "1", district = "Synthetic", city = "Synthetic", country = "Synthetic", postalCode = "00000",
            specialties = new[] { 1 }, experienceYears = 3, aboutMe = "Synthetic isolated fixture", centerLatitude = 0d, centerLongitude = 0d, radiusKm = 5d
        }, otherUser: other);
        Assert.True(complete.StatusCode == HttpStatusCode.Created, "Synthetic technician profile completion failed; no credentials logged.");
        await Login(email, password, other);
    }

    internal Func<HttpResponseMessage, Task>? ObserveCompanySignUp { get; set; }
    internal Func<HttpResponseMessage, Task>? ObserveCompanyCompletion { get; set; }
    internal async Task RegisterAndCompleteCompany(string email, string password, string taxId, bool other = false,
        Hampcoders.Electrolink.API.Profiles.Interfaces.REST.Resources.CompleteProfileAsCompanyResource? companyResource = null,
        Func<Task>? beforeCompletion = null)
    {
        using var signUp = await Send(HttpMethod.Post, "/api/v1/authentication/sign-up",
            new { email, password, passwordConfirmation = password }, anonymous: true);
        if (ObserveCompanySignUp != null) await ObserveCompanySignUp(signUp);
        Assert.Equal(HttpStatusCode.Created, signUp.StatusCode);
        var result = await signUp.Content.ReadFromJsonAsync<JsonElement>();
        SetToken(result.GetProperty("token").GetString()!, other);
        if (beforeCompletion != null) await beforeCompletion();
        using var complete = await Send(HttpMethod.Post, "/api/v1/profiles/me/complete/company", (object?)companyResource ?? new
        {
            companyName = "Synthetic Corp",
            taxId,
            billingStreet = "Synthetic",
            billingNumber = "1",
            billingDistrict = "Synthetic",
            billingCity = "Synthetic",
            billingCountry = "Synthetic",
            billingPostalCode = "00000",
            industry = "Tech",
            companySize = "Small",
            website = "https://example.invalid",
            firstName = "Synthetic",
            lastName = other ? "Other" : "Owner",
            phoneNumber = other ? "+99900000002" : "+99900000001",
            dni = other ? "00000002" : "00000001",
            dateOfBirth = "1990-01-01",
            street = "Synthetic",
            number = "1",
            district = "Synthetic",
            city = "Synthetic",
            country = "Synthetic",
            postalCode = "00000"
        }, otherUser: other);
        if (ObserveCompanyCompletion != null) await ObserveCompanyCompletion(complete);
        Assert.Equal(HttpStatusCode.Created, complete.StatusCode);
        await Login(email, password, other);
    }
    internal async Task Login(string email, string password, bool other = false)
    {
        using var response = await Send(HttpMethod.Post, "/api/v1/authentication/sign-in", new { email, password }, anonymous: true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        SetToken(result.GetProperty("token").GetString()!, other);
        using var scope = Scope();
        var user = await scope.ServiceProvider.GetRequiredService<IUserQueryService>().Handle(new GetUserByIdQuery(result.GetProperty("userId").GetString()!));
        Assert.NotNull(user);
        if (other) Other = user; else Owner = user;
    }
    internal async Task<string> CreateForOther()
    {
        async Task<JsonElement> CreateOther(string path, object body)
        { using var r = await Send(HttpMethod.Post, path, body, otherUser: true); Assert.Equal(HttpStatusCode.Created, r.StatusCode); return await r.Content.ReadFromJsonAsync<JsonElement>(); }
        var store = await CreateOther("/api/v1/stores", new { name = "Synthetic other", address = "Synthetic" });
        var root = "/api/v1/stores/" + store.GetProperty("id").GetString();
        var area = await CreateOther(root + "/areas", new { name = "Synthetic other" });
        var parent = root + "/areas/" + area.GetProperty("id").GetString();
        var item = await CreateOther(parent + "/equipment", new { name = "Synthetic other" });
        return parent + "/equipment/" + item.GetProperty("id").GetString();
    }
    private void SetToken(string token, bool other) { if (other) otherToken = token; else ownerToken = token; }
    public async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null, bool otherUser = false, bool anonymous = false, bool edgeKey = false)
    {
        using var request = new HttpRequestMessage(method, path);
        if (!anonymous) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", otherUser ? otherToken : ownerToken);
        if (edgeKey) request.Headers.Add("X-Api-Key", this.edgeKey);
        if (body != null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    public async Task<JsonElement> Create(string path, object body)
    {
        using var response = await Send(HttpMethod.Post, path, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private T Block<T>() where T : class
    {
        var proxy = DispatchProxy.Create<T, DeviceAuthorityDouble>();
        ((DeviceAuthorityDouble)(object)proxy).Call = (method, _) => { ForbiddenCalls++; throw new InvalidOperationException("Forbidden integration: " + method.Name); };
        return proxy;
    }
    public void Dispose() { client.Dispose(); server.Dispose(); }
    private sealed class ForbiddenHttpHandler(Action invoked) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { invoked(); throw new InvalidOperationException("Outbound HTTP forbidden"); }
    }
    private sealed class OnlyJourneyControllers(bool retirementEndpoint) : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var allowed = new[] { typeof(Hampcoders.Electrolink.API.IAM.Interfaces.REST.AuthenticationController),
                typeof(Hampcoders.Electrolink.API.Profiles.Interfaces.REST.ProfilesController),
                typeof(Hampcoders.Electrolink.API.StoreManagement.Interfaces.REST.StoresController),
                typeof(Hampcoders.Electrolink.API.StoreManagement.Interfaces.REST.EquipmentDeviceController),
                typeof(Hampcoders.Electrolink.API.AlertManagement.Interfaces.EquipmentAlertsController),
                typeof(Hampcoders.Electrolink.API.AlertManagement.Interfaces.StoreAlertsController),
                typeof(Hampcoders.Electrolink.API.Processing.Interfaces.REST.EdgeIngestionController) };
            foreach (var controller in feature.Controllers.ToArray()) if (!allowed.Contains(controller.AsType()) && (!retirementEndpoint || controller.AsType() != typeof(Hampcoders.Electrolink.API.Assets.Interfaces.REST.Controllers.IoTDevicesController))) feature.Controllers.Remove(controller);
        }
    }
}
