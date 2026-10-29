using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Electrolink.Backend.Characterization.Tests.Assets;
using Hampcoders.Electrolink.API.Assets.Application.ACL;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Planning.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Interfaces.REST;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Xunit;
using IAssetsContextFacade = Hampcoders.Electrolink.API.Assets.Interfaces.ACL.IAssetsContextFacade;
namespace Electrolink.Backend.Characterization.Tests.Planning;

// M3.20: GetPropertyByIdQuery does not filter by owner, so the facade must compare the property's owner itself.
public sealed class SelectPropertyOwnershipTests
{
    static Property NewProperty(ClientIdentity owner) => Property.Create(owner,
        Address.Create("Synthetic", "1", "Synthetic", "Synthetic", "Synthetic", "00000"), Geolocation.Create(-12.05, -77.04, null, "SYNTHETIC"));

    static AssetsContextFacade Facade(params Property[] properties)
    {
        var queries = AssignmentHttpFixture.Proxy<IPropertyQueryService>((m, a) =>
            Task.FromResult<Property?>(a![0] is GetPropertyByIdQuery q ? properties.SingleOrDefault(p => p.Id == q.PropertyId) : null));
        return new AssetsContextFacade(null!, null!, null!, queries, null!, null!, null!, null!, null!, null!, null!);
    }

    [Theory]
    [InlineData("ho-synthetic-m320", "ho-synthetic-m320", true)]
    [InlineData("comp-synthetic-m320", "comp-synthetic-m320", true)]
    [InlineData("ho-other-m320", "ho-synthetic-m320", false)]
    [InlineData("comp-other-m320", "ho-synthetic-m320", false)]
    [InlineData("ho-synthetic-m320", "comp-synthetic-m320", false)]
    public async Task Facade_owner_lookups_answer_only_for_the_propertys_owner(string owner, string asker, bool found)
    {
        var property = NewProperty(ClientIdentity.FromOwnerId(owner));
        var facade = Facade(property);
        Assert.Equal(found, (await facade.GetPropertyGeolocationAsync(property.Id.Value, asker)).HasValue);
        Assert.Equal(found, await facade.PropertyBelongsToOwnerAsync(property.Id.Value, asker));
    }

    [Theory]
    [InlineData("own", 204)]
    [InlineData("foreign", 404)]
    [InlineData("missing", 404)]
    public async Task Select_property_accepts_only_the_clients_own_property(string condition, int status)
    {
        using var h = new SelectPropertyFixture(condition);
        using var response = await h.Send();
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        if (condition == "own")
        {
            Assert.Equal(ERequestStatus.PropertySelected, h.Request.Status); Assert.Equal(h.Own.Id.Value, h.Request.PropertyId!.Value);
            Assert.Equal(-12.05, h.Request.Geolocation!.Latitude); Assert.Equal(1, h.Saves);
        }
        else
        {
            Assert.Equal(ERequestStatus.Draft, h.Request.Status); Assert.Null(h.Request.PropertyId); Assert.Null(h.Request.Geolocation);
            Assert.Equal(0, h.Saves);
        }
        if (condition != "own") Assert.Equal("{\"message\":\"Resource not found.\"}", await response.Content.ReadAsStringAsync());
    }

    private sealed class SelectPropertyFixture : IDisposable
    {
        internal const string Client = "ho-synthetic-m320";
        internal ServiceRequest Request { get; } = ServiceRequest.Initiate(ClientIdentity.FromHomeowner(Client), false, null);
        internal Property Own { get; } = NewProperty(ClientIdentity.FromHomeowner(Client));
        internal int Saves { get; private set; }
        private readonly Property foreign = NewProperty(ClientIdentity.FromHomeowner("ho-other-m320"));
        private readonly string condition;
        private readonly TestServer server;
        private readonly HttpClient client;

        internal SelectPropertyFixture(string condition)
        {
            this.condition = condition;
            var requests = AssignmentHttpFixture.Proxy<IServiceRequestRepository>((m, a) => m.Name switch
            {
                nameof(IServiceRequestRepository.FindByIdAsync) => Task.FromResult<ServiceRequest?>((RequestId)a![0]! == Request.RequestId ? Request : null),
                nameof(IServiceRequestRepository.Update) => null,
                _ => throw new InvalidOperationException("Unexpected request call " + m.Name)
            });
            var unit = AssignmentHttpFixture.Proxy<IUnitOfWork>((m, a) => { Saves++; return Task.CompletedTask; });
            // Only the collaborators SelectProperty uses are present; the rest would fail if touched.
            var service = new ServiceRequestCommandService(requests, null!, null!, null!, null!, Facade(Own, foreign), null!, unit,
                NullLogger<ServiceRequestCommandService>.Instance);
            // M3.29: the controller checks the request's owner with the summary query (real semantics: same client only).
            var queries = AssignmentHttpFixture.Proxy<IServiceDesignQueryService>((m, a) => a![0] is GetServiceRequestSummaryQuery q
                ? Task.FromResult<ServiceRequest?>(q.RequestId == Request.RequestId && q.Client == Request.Client ? Request : null)
                : throw new InvalidOperationException("Unexpected query"));
            // M3.29: the client comes from the IAM user and Profiles (an active, concordant HomeOwner), not from JWT claims.
            var actor = User.Create(Email.From("m320-client@example.invalid"), HashedPassword.FromHash("synthetic"), AccessRole.User);
            var profiles = AssignmentHttpFixture.Proxy<IProfilesContextFacade>((m, a) => m.Name switch
            {
                nameof(IProfilesContextFacade.GetProfileClaimsAsync) => Task.FromResult<(string ProfileId, string ProfileStatus, string? BusinessRole, string? RoleSubjectId, string? SubscriptionTier)?>(
                    ("prof-synthetic-m320", "Active", "HomeOwner", Client, null)),
                nameof(IProfilesContextFacade.GetProfileIdByHomeownerIdAsync) => Task.FromResult<string?>("prof-synthetic-m320"),
                _ => throw new InvalidOperationException("Unexpected profile call " + m.Name)
            });
            server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
            {
                services.AddSingleton<IServiceRequestCommandService>(service); services.AddSingleton(queries);
                services.AddSingleton(new ExternalProfilesService(profiles));
                services.AddControllers(o => o.Conventions.Add(new Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Configuration.KebabCaseRouteNamingConvention()))
                    .AddApplicationPart(typeof(ServiceRequestController).Assembly).ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlyServiceRequestController()));
            }).Configure(app =>
            {
                app.UseRouting(); app.Use(async (context, next) =>
                {
                    context.Items["User"] = actor;
                    await next();
                }); app.UseEndpoints(e => e.MapControllers());
            })); client = server.CreateClient();
        }

        internal Task<HttpResponseMessage> Send()
        {
            var property = condition switch { "own" => Own.Id.Value, "foreign" => foreign.Id.Value, _ => "prop-missing-synthetic-m320" };
            return client.PostAsync("/api/v1/service-requests/" + Request.RequestId.Value + "/property", JsonContent.Create(new { propertyId = property }));
        }

        public void Dispose() { client.Dispose(); server.Dispose(); }

        private sealed class OnlyServiceRequestController : IApplicationFeatureProvider<ControllerFeature>
        {
            public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
            { foreach (var c in feature.Controllers.ToArray()) if (c.AsType() != typeof(ServiceRequestController)) feature.Controllers.Remove(c); }
        }
    }
}
