using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Electrolink.Backend.Characterization.Tests.Assets;
using Hampcoders.Electrolink.API.Assets.Application.ACL;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Planning.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Interfaces.REST;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IAssetsContextFacade = Hampcoders.Electrolink.API.Assets.Interfaces.ACL.IAssetsContextFacade;
namespace Electrolink.Backend.Characterization.Tests.Planning;

// M3.19: the subscription's Company attaches its own property; matching is the existing algorithm.
public sealed class SubscriptionInstallationPropertyTests
{
    internal const string Company = "comp-synthetic-m319";
    internal static ServiceRequest SubscriptionRequest(string userId) =>
        ServiceRequest.CreateIoTInstallationRequest("sub-synthetic-m319", userId, ClientIdentity.FromCompany(Company), 2, DateTime.UtcNow.AddDays(30));

    [Fact]
    public void Attach_prepares_pending_assignment_with_client_property_geolocation_and_installation_category()
    {
        var request = SubscriptionRequest("us-synthetic-m319");
        request.AttachInstallationProperty(PropertyId.From("prop-synthetic-m319"), Geolocation.Create(-12.05, -77.04, null, "PROPERTY"));
        Assert.Equal(ERequestStatus.PendingAssignment, request.Status);
        Assert.Equal(ClientIdentity.FromCompany(Company), request.Client);
        Assert.Equal("prop-synthetic-m319", request.PropertyId!.Value);
        Assert.Equal(-12.05, request.Geolocation!.Latitude); Assert.Equal(-77.04, request.Geolocation.Longitude);
        Assert.Equal(EServiceCategory.Installation, request.RequestedCategory);
        Assert.Equal(2, request.RequiredDeviceCount); Assert.Null(request.SelectedTechnicianId);
        Assert.Empty(request.DomainEvents);
    }

    [Fact]
    public void Attach_is_rejected_outside_pending_installation_and_never_overwrites_the_property()
    {
        var request = SubscriptionRequest("us-synthetic-m319");
        request.AttachInstallationProperty(PropertyId.From("prop-first-m319"), Geolocation.Create(0, 0, null, "PROPERTY"));
        Assert.Throws<InvalidRequestStatusException>(() => request.AttachInstallationProperty(PropertyId.From("prop-second-m319"), Geolocation.Create(1, 1, null, "PROPERTY")));
        Assert.Equal("prop-first-m319", request.PropertyId!.Value); Assert.Equal(0, request.Geolocation!.Latitude);
        var draft = ServiceRequest.Initiate(ClientIdentity.FromCompany(Company), false, null);
        Assert.Throws<InvalidRequestStatusException>(() => draft.AttachInstallationProperty(PropertyId.From("prop-first-m319"), Geolocation.Create(0, 0, null, "PROPERTY")));
        Assert.Null(draft.PropertyId); Assert.Equal(ERequestStatus.Draft, draft.Status);
    }

    [Fact]
    public void Subscription_request_requires_its_company_client()
        => Assert.Throws<ArgumentNullException>(() => ServiceRequest.CreateIoTInstallationRequest("sub-synthetic-m319", "us-synthetic-m319", null!, 1, DateTime.UtcNow));

    [Theory]
    [InlineData("owned", true)]
    [InlineData("foreign", false)]
    [InlineData("inactive", false)]
    [InlineData("archived", false)]
    public async Task Assets_facade_returns_geolocation_only_for_an_active_unarchived_property_of_that_owner(string condition, bool found)
    {
        var owner = ClientIdentity.FromCompany(condition == "foreign" ? "comp-other-m319" : Company);
        var property = Property.Create(owner, Address.Create("Synthetic", "1", "Synthetic", "Synthetic", "Synthetic", "00000"), Geolocation.Create(-12.05, -77.04, null, "SYNTHETIC"));
        if (condition == "inactive") typeof(Property).GetMethod("Deactivate", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(property, null);
        if (condition == "archived") typeof(Property).GetMethod("Archive", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(property, ["synthetic"]);
        var queries = AssignmentHttpFixture.Proxy<IPropertyQueryService>((m, a) => a![0] is GetPropertyByIdQuery q && q.PropertyId == property.Id
            ? Task.FromResult<Property?>(property) : Task.FromResult<Property?>(null));
        var facade = new AssetsContextFacade(null!, null!, null!, queries, null!, null!, null!, null!, null!, null!, null!);
        var geo = await facade.GetActiveOwnedPropertyGeolocationAsync(property.Id.Value, ClientIdentity.FromCompany(Company));
        Assert.Equal(found, geo.HasValue);
        if (found) { Assert.Equal(-12.05, geo!.Value.Latitude); Assert.Equal(-77.04, geo.Value.Longitude); }
    }

    [Fact]
    public async Task Owner_company_attaches_own_property_and_matching_runs_once_without_quota()
    {
        using var h = new InstallationPropertyFixture("valid");
        using var response = await h.Send();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "matching", "propertyId", "requestId", "status" }, body.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(h.Request.RequestId.Value, body.GetProperty("requestId").GetString());
        Assert.Equal(InstallationPropertyFixture.Property, body.GetProperty("propertyId").GetString());
        Assert.Equal("Assigned", body.GetProperty("matching").GetString()); Assert.Equal("Assigned", body.GetProperty("status").GetString());
        Assert.Equal(ClientIdentity.FromCompany(Company), h.Request.Client); Assert.Equal(EServiceCategory.Installation, h.Request.RequestedCategory);
        Assert.Equal(-12.05, h.Request.Geolocation!.Latitude);
        // Saved as PendingAssignment before the existing matching, which advanced it.
        Assert.Equal(new[] { ERequestStatus.PendingAssignment }, h.StatusAtSave); Assert.Equal(1, h.Matchings);
        Assert.Equal(0, h.QuotaOrLegacyCalls);
    }

    [Theory]
    [InlineData("foreign-company", 404)]
    [InlineData("foreign-user-same-company", 404)]
    [InlineData("homeowner", 403)]
    [InlineData("technician", 403)]
    [InlineData("admin-without-profile", 403)]
    [InlineData("suspended", 403)]
    [InlineData("inactive-profile", 403)]
    [InlineData("subject-mismatch", 403)]
    [InlineData("anonymous", 401)]
    [InlineData("invalid-token", 401)]
    [InlineData("edge", 401)]
    [InlineData("unavailable-property", 404)]
    [InlineData("missing-request", 404)]
    [InlineData("malformed-request-id", 404)]
    [InlineData("no-subscription", 404)]
    [InlineData("already-prepared", 409)]
    [InlineData("missing-body-property", 400)]
    [InlineData("malformed-property", 400)]
    public async Task Rejected_attachment_changes_nothing_saves_nothing_and_runs_no_matching(string condition, int status)
    {
        using var h = new InstallationPropertyFixture(condition);
        var before = h.Snapshot();
        using var response = await h.Send();
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal(before, h.Snapshot()); Assert.Equal(0, h.Saves); Assert.Equal(0, h.Matchings); Assert.Equal(0, h.QuotaOrLegacyCalls);
        Assert.Empty(h.Request.DomainEvents);
    }

    [Fact]
    public async Task Repeated_call_is_409_without_new_transition_matching_or_overwrite()
    {
        using var h = new InstallationPropertyFixture("valid");
        using (var first = await h.Send()) Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var after = h.Snapshot();
        using var second = await h.Send(property: "prop-second-synthetic-m319");
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(after, h.Snapshot()); Assert.Equal(1, h.Saves); Assert.Equal(1, h.Matchings);
    }

    [Fact]
    public async Task No_candidates_is_reported_explicitly_and_the_prepared_request_stays_persisted()
    {
        using var h = new InstallationPropertyFixture("no-candidates");
        using var response = await h.Send();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NoCandidates", body.GetProperty("matching").GetString()); Assert.Equal("PendingAssignment", body.GetProperty("status").GetString());
        Assert.Equal(1, h.Saves); Assert.Equal(1, h.Matchings); Assert.Equal(ERequestStatus.PendingAssignment, h.Request.Status);
    }

    [Fact]
    public async Task Unexpected_matching_failure_surfaces_after_the_preparation_was_saved()
    {
        using var h = new InstallationPropertyFixture("matching-fails");
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Send());
        // The preparation is not rolled back: the failure is visible, not a no-op.
        Assert.Equal(1, h.Saves); Assert.Equal(1, h.Matchings); Assert.Equal(ERequestStatus.PendingAssignment, h.Request.Status);
    }

    [Fact]
    public void Service_request_status_is_an_ef_concurrency_token_without_new_columns()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration.AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused", pg => pg.UseNetTopologySuite()).Options;
        using var context = new Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration.AppDbContext(options);
        var entity = context.Model.FindEntityType(typeof(ServiceRequest))!;
        Assert.True(entity.FindProperty(nameof(ServiceRequest.Status))!.IsConcurrencyToken);
        Assert.Equal("status", entity.FindProperty(nameof(ServiceRequest.Status))!.GetColumnName());
    }
}

// Synthetic identity/persistence boundary; JWT, Profiles, Assets, Planning and EF are verified in the PostgreSQL mode.
internal sealed class InstallationPropertyFixture : IDisposable
{
    internal const string Property = "prop-synthetic-m319";
    internal ServiceRequest Request { get; }
    internal int Saves { get; private set; }
    internal int Matchings { get; private set; }
    internal int QuotaOrLegacyCalls { get; private set; }
    internal List<ERequestStatus> StatusAtSave { get; } = [];
    private readonly string condition;
    private readonly TestServer server;
    private readonly HttpClient client;

    internal InstallationPropertyFixture(string condition)
    {
        this.condition = condition;
        var actor = User.Create(Email.From("m319-company@example.invalid"), HashedPassword.FromHash("synthetic"), AccessRole.Admin);
        if (condition == "suspended") actor.Suspend("synthetic");
        var owner = condition == "foreign-user-same-company" ? "us-other-synthetic-m319" : actor.Id.Value;
        Request = condition == "no-subscription" ? ServiceRequest.Initiate(ClientIdentity.FromCompany(SubscriptionInstallationPropertyTests.Company), false, null)
            : SubscriptionInstallationPropertyTests.SubscriptionRequest(owner);
        if (condition == "already-prepared")
            Request.AttachInstallationProperty(PropertyId.From(Property), Geolocation.Create(0, 0, null, "PROPERTY"));
        Request.ClearDomainEvents();
        var subject = condition == "foreign-company" ? "comp-other-synthetic-m319" : SubscriptionInstallationPropertyTests.Company;
        var profiles = AssignmentHttpFixture.Proxy<IProfilesContextFacade>((m, a) => m.Name switch
        {
            nameof(IProfilesContextFacade.GetProfileClaimsAsync) => Task.FromResult<(string ProfileId, string ProfileStatus, string? BusinessRole, string? RoleSubjectId, string? SubscriptionTier)?>(
                condition switch
                {
                    "admin-without-profile" => null,
                    "homeowner" => ("prof-synthetic-m319", "Active", "Homeowner", "ho-synthetic-m319", null),
                    "technician" => ("prof-synthetic-m319", "Active", "Technician", "tech-synthetic-m319", null),
                    "inactive-profile" => ("prof-synthetic-m319", "Deactivated", "Company", subject, null),
                    _ => ("prof-synthetic-m319", "Active", "Company", subject, null)
                }),
            nameof(IProfilesContextFacade.GetProfileIdByCompanyIdAsync) => Task.FromResult<string?>(condition == "subject-mismatch" ? "prof-other-m319" : "prof-synthetic-m319"),
            _ => throw new InvalidOperationException("Unexpected profile call " + m.Name)
        });
        var assets = AssignmentHttpFixture.Proxy<IAssetsContextFacade>((m, a) => m.Name == nameof(IAssetsContextFacade.GetActiveOwnedPropertyGeolocationAsync)
            ? Task.FromResult<(double Latitude, double Longitude)?>(condition != "unavailable-property" && (string)a![0]! == Property
                && (ClientIdentity)a[1]! == ClientIdentity.FromCompany(subject) ? (-12.05, -77.04) : null)
            : throw new InvalidOperationException("Unexpected assets call " + m.Name));
        var requests = AssignmentHttpFixture.Proxy<IServiceRequestRepository>((m, a) => m.Name switch
        {
            nameof(IServiceRequestRepository.FindByIdAsync) => Task.FromResult<ServiceRequest?>(condition != "missing-request" && (RequestId)a![0]! == Request.RequestId ? Request : null),
            nameof(IServiceRequestRepository.Update) => null,
            _ => throw new InvalidOperationException("Unexpected request call " + m.Name)
        });
        var unit = AssignmentHttpFixture.Proxy<IUnitOfWork>((m, a) => { Saves++; StatusAtSave.Add(Request.Status); return Task.CompletedTask; });
        var matching = AssignmentHttpFixture.Proxy<IServiceAssignmentCommandService>((m, a) =>
        {
            Matchings++;
            if (condition == "no-candidates") throw new NoCandidatesAvailableException("NO_CANDIDATES_AVAILABLE");
            if (condition == "matching-fails") throw new InvalidOperationException("synthetic matching failure");
            Request.MarkAsAssigned(AssignmentId.NewAssignmentId(), TechnicianId.From("tech-synthetic-m319"), RecipeSnapshot.Create(RecipeId.NewId(), "Synthetic installation",
                EServiceCategory.Installation, [], ServicePricing.Create(Money.Of(0, ECurrency.Pen), Money.Of(1, ECurrency.Pen), Money.Of(1, ECurrency.Pen)),
                EstimatedDuration.FromMinutes(1), WarrantyPeriod.OfMonths(0), DateTime.UtcNow));
            return Task.CompletedTask;
        });
        var legacy = AssignmentHttpFixture.Proxy<IServiceRequestCommandService>((m, a) => { QuotaOrLegacyCalls++; throw new InvalidOperationException("Legacy request flow is not part of this operation"); });
        var queries = AssignmentHttpFixture.Proxy<IServiceDesignQueryService>((m, a) => { QuotaOrLegacyCalls++; throw new InvalidOperationException("Quota/query flow is not part of this operation"); });
        server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton(requests); services.AddSingleton(unit); services.AddSingleton(matching);
            services.AddSingleton(new ExternalProfilesService(profiles)); services.AddSingleton(new ExternalAssetsService(assets));
            services.AddSingleton(legacy); services.AddSingleton(queries);
            services.AddScoped<SubscriptionInstallationPropertyService>();
            services.AddControllers(o => o.Conventions.Add(new Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Configuration.KebabCaseRouteNamingConvention()))
                .AddApplicationPart(typeof(ServiceRequestController).Assembly).ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlyServiceRequestController()));
        }).Configure(app =>
        {
            app.UseRouting(); app.Use(async (context, next) =>
            {
                if (context.Request.Headers.Authorization == "Bearer synthetic-authorized") context.Items["User"] = actor;
                await next();
            }); app.UseEndpoints(e => e.MapControllers());
        })); client = server.CreateClient();
    }

    internal string Snapshot() => JsonSerializer.Serialize(new
    {
        Status = Request.Status.ToString(), Client = Request.Client?.ToString(), Property = Request.PropertyId?.Value,
        Request.Geolocation?.Latitude, Category = Request.RequestedCategory?.ToString(), Technician = Request.SelectedTechnicianId?.Value
    });

    internal Task<HttpResponseMessage> Send(string? property = null)
    {
        var id = condition == "malformed-request-id" ? "not-a-request" : Request.RequestId.Value;
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/service-requests/" + id + "/installation-property");
        if (condition is not ("anonymous" or "edge" or "invalid-token")) request.Headers.TryAddWithoutValidation("Authorization", "Bearer synthetic-authorized");
        if (condition == "invalid-token") request.Headers.TryAddWithoutValidation("Authorization", "Bearer synthetic-invalid");
        if (condition == "edge") request.Headers.Add("X-Api-Key", "synthetic-edge-only");
        request.Content = condition switch
        {
            "missing-body-property" => JsonContent.Create(new { }),
            "malformed-property" => JsonContent.Create(new { propertyId = "not-a-property" }),
            // Identity fields in the body are ignored; the server decides client, user, technician and subscription.
            _ => JsonContent.Create(new { propertyId = property ?? Property, clientId = "comp-forged-m319", userId = "us-forged-m319", technicianId = "tech-forged-m319", subscriptionId = "sub-forged-m319" })
        };
        return client.SendAsync(request);
    }

    public void Dispose() { client.Dispose(); server.Dispose(); }

    private sealed class OnlyServiceRequestController : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        { foreach (var c in feature.Controllers.ToArray()) if (c.AsType() != typeof(ServiceRequestController)) feature.Controllers.Remove(c); }
    }
}
