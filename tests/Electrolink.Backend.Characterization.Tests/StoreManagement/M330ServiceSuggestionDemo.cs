using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Hampcoders.Electrolink.API.Planning.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Planning.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Infrastructure.Persistence.EFC.Repositories;
using Hampcoders.Electrolink.API.Planning.Interfaces.REST;
using Hampcoders.Electrolink.API.Profiles.Domain.Repositories;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

// M3.30: ServiceSuggestionController with real IAM/JWT/Profiles, real suggestion services and EF/Npgsql.
// Suggestions are produced by anomaly handlers in the product; here they are seeded through the real repository.
public static class M330ServiceSuggestionDemo
{
    public static async Task Run(string manifestPath)
    {
        var connection = await M319SubscriptionInstallationDemo.Connect(manifestPath);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath)); var m = json.RootElement;
        var task = m.GetProperty("taskId").GetString()!;
        using var h = new RealBackendScenario(connection, m.GetProperty("jwtSecret").GetString()!, m.GetProperty("edgeKey").GetString()!, "unused-synthetic-m330",
            machineScope: false, extraControllers: [typeof(ServiceSuggestionController)], extraServices: services =>
            {
                services.AddScoped<IServiceSuggestionRepository, ServiceSuggestionRepository>();
                services.AddScoped<ExternalProfilesService>();
                services.AddScoped<IServiceSuggestionCommandService, ServiceSuggestionCommandService>();
                services.AddScoped<IServiceSuggestionQueryService, ServiceSuggestionQueryService>();
            });
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + "aA1!";
        await h.RegisterAndComplete("m330-other-" + task + "@example.invalid", password, "00000002", other: true);
        await h.RegisterAndComplete("m330-owner-" + task + "@example.invalid", password, "00000001");
        string owner, other, ownId, otherId;
        using (var seed = h.Scope())
        {
            var sp = seed.ServiceProvider; var profiles = sp.GetRequiredService<IProfilesContextFacade>();
            owner = (await profiles.GetProfileClaimsAsync(h.Owner.Id.Value))!.Value.RoleSubjectId!;
            other = (await profiles.GetProfileClaimsAsync(h.Other.Id.Value))!.Value.RoleSubjectId!;
            ServiceSuggestion Make(string client) => ServiceSuggestion.Create(client, null, "prop-synthetic-m330", "anom-synthetic-m330",
                "dev-synthetic-m330", "OVERCURRENT", "CRITICAL", DateTime.UtcNow.AddDays(1));
            var own = Make(owner); var foreign = Make(other); own.ClearDomainEvents(); foreign.ClearDomainEvents();
            ownId = own.SuggestionId; otherId = foreign.SuggestionId;
            var repository = sp.GetRequiredService<IServiceSuggestionRepository>();
            await repository.AddAsync(own); await repository.AddAsync(foreign);
            await sp.GetRequiredService<IUnitOfWork>().CompleteAsync();
        }
        async Task<string> Status(string id)
        {
            using var scope = h.Scope();
            return (await scope.ServiceProvider.GetRequiredService<IServiceSuggestionRepository>().FindBySuggestionIdAsync(id))!.Status.ToString();
        }
        const string root = "/api/v1/suggestions";
        using (var r = await h.Send(HttpMethod.Get, root))
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal(ownId, Assert.Single((await r.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()).GetProperty("suggestionId").GetString());
        }
        // The other HomeOwner cannot view, accept or dismiss the owner's suggestion.
        foreach (var path in new[] { root + "/" + ownId + "/viewed", root + "/" + ownId + "/accept", root + "/" + ownId + "/dismiss" })
        {
            using var r = await h.Send(path.EndsWith("viewed") ? HttpMethod.Patch : HttpMethod.Post, path, otherUser: true);
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        }
        Assert.Equal("Pending", await Status(ownId));
        using (var r = await h.Send(HttpMethod.Patch, root + "/" + ownId + "/viewed")) Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        string requestId;
        using (var r = await h.Send(HttpMethod.Post, root + "/" + ownId + "/accept"))
        { Assert.Equal(HttpStatusCode.OK, r.StatusCode); requestId = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestId").GetString()!; }
        using (var r = await h.Send(HttpMethod.Post, root + "/" + ownId + "/accept")) Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("ConvertedToRequest", await Status(ownId));
        using (var scope = h.Scope())
            Assert.Equal(ClientIdentity.FromHomeowner(owner), (await scope.ServiceProvider.GetRequiredService<IServiceRequestRepository>()
                .FindByIdAsync(Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects.RequestId.From(requestId)))!.Client);
        // After the other HomeOwner's profile is suspended, its still-valid token cannot act on its own suggestion.
        using (var scope = h.Scope())
        {
            var profile = await scope.ServiceProvider.GetRequiredService<IProfileRepository>().FindByUserIdAsync(UserId.From(h.Other.Id.Value));
            profile!.Suspend(); profile.ClearDomainEvents(); await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CompleteAsync();
        }
        using (var r = await h.Send(HttpMethod.Post, root + "/" + otherId + "/dismiss", otherUser: true)) Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("Pending", await Status(otherId));
        using (var edge = await h.Send(HttpMethod.Get, root, anonymous: true, edgeKey: true)) Assert.Equal(HttpStatusCode.Unauthorized, edge.StatusCode);
        Assert.Equal(0, h.ForbiddenCalls);
        Console.WriteLine("PASS M3.30 real suggestions: owner lists/views/accepts (request persisted with the owner's client; repeat 409); other HomeOwner 404; suspended profile's token 403; Edge 401");
    }
}
