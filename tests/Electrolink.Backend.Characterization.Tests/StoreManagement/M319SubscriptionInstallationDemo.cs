using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Commands;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Planning.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Planning.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Infrastructure.Persistence.EFC.Repositories;
using Hampcoders.Electrolink.API.Planning.Interfaces.ACL;
using Hampcoders.Electrolink.API.Planning.Interfaces.REST;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Infrastructure;
using Hampcoders.Electrolink.API.Shared.Infrastructure.ExternalProviders;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Events;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.ACL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

// M3.19: subscription request created by the real Planning handler, property attached by the Company over HTTP,
// existing matching (AI provider blocked → deterministic fallback), then the technician's own-stock assign/install.
public static class M319SubscriptionInstallationDemo
{
    internal static async Task<string> Connect(string manifestPath)
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath)); var m = json.RootElement;
        string Read(string key) => m.GetProperty(key).GetString()!;
        var task = Read("taskId"); var db = Read("database"); var host = Read("host"); var port = m.GetProperty("port").GetInt32();
        if (!Regex.IsMatch(task, "^[0-9a-f]{32}$") || db != "electrolink_m33_" + task || host != "127.0.0.1" || port is < 1024 or > 65535)
            throw new InvalidOperationException("Unrecognized disposable destination");
        var connection = new NpgsqlConnectionStringBuilder { Host = host, Port = port, Database = db, Username = "postgres", Password = Read("password"),
            Pooling = false, Timeout = 5, CommandTimeout = 30, IncludeErrorDetail = false }.ConnectionString;
        await using var pg = new NpgsqlConnection(connection);
        await pg.OpenAsync(); await using var command = new NpgsqlCommand("SELECT shobj_description(oid,'pg_database') FROM pg_database WHERE datname=current_database()", pg);
        Assert.Equal("electrolink-m33:" + task, await command.ExecuteScalarAsync());
        return connection;
    }

    public static async Task Run(string manifestPath)
    {
        // Same process-wide Npgsql setting as Program.cs (Planning catalog jsonb lists); this mode runs in its own process.
#pragma warning disable CS0618
        NpgsqlConnection.GlobalTypeMapper.EnableDynamicJson();
#pragma warning restore CS0618
        var connection = await Connect(manifestPath);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath)); var m = json.RootElement;
        var task = m.GetProperty("taskId").GetString()!;
        var counters = new Counters(); var gate = new AttachRaceGate();
        using var h = new RealBackendScenario(connection, m.GetProperty("jwtSecret").GetString()!, m.GetProperty("edgeKey").GetString()!, "unused-synthetic-m319",
            machineScope: false, includeRetirementEndpoint: true, includeAssignmentHandler: true, includeInstallationHandler: true, saveInterceptor: gate,
            extraServices: services =>
            {
                services.AddScoped<IServiceCatalogRepository, ServiceCatalogRepository>();
                services.AddScoped<ExternalProfilesService>(); services.AddScoped<ExternalAssetsService>();
                services.AddScoped<IServiceAssignmentCommandService, ServiceAssignmentCommandService>();
                services.AddScoped<IMatchingService, HybridMatchingService>();
                services.Configure<OpenAISettings>(o => { o.MaxCandidatesForAI = 5; o.MatchingTimeoutSeconds = 1; });
                services.AddSingleton(counters.Block<IAIMatchingProvider>(() => counters.AiCalls++));
                services.AddSingleton(counters.Block<IServiceRequestCommandService>(() => counters.LegacyOrQuota++));
                services.AddSingleton(counters.Block<IServiceDesignQueryService>(() => counters.LegacyOrQuota++));
                services.AddScoped<SubscriptionInstallationPropertyService>();
            }, extraControllers: [typeof(ServiceRequestController)]);
        using (var empty = h.Scope())
        {
            var ctx = empty.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0, await ctx.Set<Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates.User>().CountAsync());
            Assert.Equal(0, await ctx.Set<ServiceRequest>().CountAsync());
        }
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + "aA1!";
        string Email(string who) => "m319-" + who + "-" + task + "@example.invalid";

        // Technician first (own stock and Installation recipe), then the second Company, then the owner Company last as the default token.
        await h.RegisterAndCompleteTechnician(Email("tech"), password, "00000003", other: true);
        var technicianUser = h.Other;
        await h.RegisterAndCompleteCompany(Email("other-company"), password, "20000000002", other: true);
        await h.RegisterAndCompleteCompany(Email("company"), password, "20000000001");
        var companyUser = h.Owner;
        string technician, company, otherCompany, ownProperty, secondOwnProperty, farProperty, foreignProperty;
        using (var seed = h.Scope())
        {
            var sp = seed.ServiceProvider; var profiles = sp.GetRequiredService<IProfilesContextFacade>();
            technician = (await profiles.GetTechnicianIdByUserIdAsync(technicianUser.Id.Value))!;
            company = (await profiles.GetProfileClaimsAsync(companyUser.Id.Value))!.Value.RoleSubjectId!;
            otherCompany = (await profiles.GetProfileClaimsAsync(h.Other.Id.Value))!.Value.RoleSubjectId!;
            Assert.StartsWith("comp-", company); Assert.NotEqual(company, otherCompany);
            var properties = sp.GetRequiredService<IPropertyCommandService>();
            async Task<string> Property(string owner, double lat, double lon) => (await properties.Handle(new CreatePropertyCommand(ClientIdentity.FromCompany(owner),
                Address.Create("Synthetic", "1", "Synthetic", "Synthetic", "Synthetic", "00000"), Geolocation.Create(lat, lon, null, "SYNTHETIC"), EPropertyType.Commercial)))!.Id.Value;
            // Technician's service area is centred on (0,0) with a 5 km radius.
            ownProperty = await Property(company, 0.01, 0.01); secondOwnProperty = await Property(company, 0.02, 0.02);
            farProperty = await Property(company, 10, 10); foreignProperty = await Property(otherCompany, 0.01, 0.01);
            // Technical setup of the technician's Installation recipe (catalog HTTP is not part of this cut).
            var catalog = ServiceCatalog.Create(TechnicianId.From(technician));
            catalog.AddRecipe("Synthetic IoT installation", "Synthetic", EServiceCategory.Installation, false, [], EstimatedDuration.FromMinutes(30),
                ServicePricing.Create(Money.Of(0, ECurrency.Pen), Money.Of(1, ECurrency.Pen), Money.Of(1, ECurrency.Pen)), [], [], WarrantyPeriod.OfMonths(0), null!);
            catalog.ClearDomainEvents();
            await sp.GetRequiredService<IServiceCatalogRepository>().AddAsync(catalog);
            await Diagnosed(() => sp.GetRequiredService<IUnitOfWork>().CompleteAsync(), "catalog setup");
        }

        // 1. Persistence: the real creation handler with the real Profiles ACL; only the Subscriptions facade is a recorder.
        async Task<string> CreateRequest(string subscription)
        {
            var recorded = new List<(string, string)>();
            using (var scope = h.Scope())
            {
                var sp = scope.ServiceProvider;
                var facade = AssignmentHttpFixtureProxy<ISubscriptionContextFacade>((mi, a) => { recorded.Add(((string)a![0]!, (string)a[1]!)); return Task.CompletedTask; });
                var handler = new EnterpriseSubscriptionPendingInstallationEventHandler(sp.GetRequiredService<IServiceRequestRepository>(), sp.GetRequiredService<IUnitOfWork>(),
                    facade, sp.GetRequiredService<ExternalProfilesService>(), NullLogger<EnterpriseSubscriptionPendingInstallationEventHandler>.Instance);
                await handler.Handle(new EnterpriseSubscriptionPendingInstallationEvent(subscription, companyUser.Id.Value, "sub_synthetic_m319", 2, 10,
                    DateTime.UtcNow.AddDays(30), DateTime.UtcNow), default);
            }
            var (sub, id) = Assert.Single(recorded); Assert.Equal(subscription, sub);
            using var fresh = h.Scope();
            var stored = await fresh.ServiceProvider.GetRequiredService<AppDbContext>().Set<ServiceRequest>().AsNoTracking().SingleAsync(r => r.RequestId == RequestId.From(id));
            Assert.Equal(ERequestStatus.PendingInstallation, stored.Status); Assert.Equal(ClientIdentity.FromCompany(company), stored.Client);
            Assert.Equal(companyUser.Id.Value, stored.UserId); Assert.Equal(subscription, stored.SubscriptionId); Assert.Equal(2, stored.RequiredDeviceCount);
            Assert.Null(stored.PropertyId); Assert.Null(stored.Geolocation); Assert.Null(stored.RequestedCategory);
            return id;
        }
        var requestId = await CreateRequest("sub-synthetic-m319-" + task);
        Console.WriteLine("PASS M3.19 subscription PendingInstallation request persists with the owner's Company client resolved by Profiles");

        var path = "/api/v1/service-requests/" + requestId + "/installation-property";
        async Task<string> State(string id)
        {
            using var scope = h.Scope(); var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var r = await ctx.Set<ServiceRequest>().AsNoTracking().SingleAsync(x => x.RequestId == RequestId.From(id));
            var assignments = await ctx.Set<ServiceAssignment>().AsNoTracking().CountAsync(a => a.RequestId == RequestId.From(id));
            return JsonSerializer.Serialize(new { Status = r.Status.ToString(), Property = r.PropertyId?.Value, r.Geolocation?.Latitude,
                Category = r.RequestedCategory?.ToString(), Tech = r.SelectedTechnicianId?.Value, Client = r.Client?.ToString(), assignments });
        }
        async Task Denied(object body, HttpStatusCode code, bool other = false, bool anonymous = false, bool edge = false, string? url = null)
        {
            var before = await State(requestId); var ai = counters.AiCalls;
            using var response = await h.Send(HttpMethod.Post, url ?? path, body, otherUser: other, anonymous: anonymous, edgeKey: edge);
            Assert.Equal(code, response.StatusCode);
            Assert.Equal(before, await State(requestId)); Assert.Equal(ai, counters.AiCalls);
        }
        object Body(string property) => new { propertyId = property, clientId = "comp-forged-m319", userId = "us-forged-m319", technicianId = "tech-forged-m319", subscriptionId = "sub-forged-m319" };

        // 2. Rejections leave the persisted request, assignments and matching untouched.
        await Denied(Body(ownProperty), HttpStatusCode.Unauthorized, anonymous: true);
        await Denied(Body(ownProperty), HttpStatusCode.Unauthorized, anonymous: true, edge: true);
        await Denied(Body(ownProperty), HttpStatusCode.NotFound, other: true);                 // other Company
        await Denied(Body(foreignProperty), HttpStatusCode.NotFound);                          // property of another Company
        await Denied(new { }, HttpStatusCode.BadRequest);
        await Denied(new { propertyId = "not-a-property" }, HttpStatusCode.BadRequest);
        await Denied(Body(ownProperty), HttpStatusCode.NotFound, url: "/api/v1/service-requests/req-missing-synthetic-m319/installation-property");
        await h.Login(Email("tech"), password, other: true);
        await Denied(Body(ownProperty), HttpStatusCode.Forbidden, other: true);                // Technician
        using (var seed = h.Scope())
            await seed.ServiceProvider.GetRequiredService<IStaffUserCommandService>().Handle(new CreateStaffUserCommand(Email("admin"), password, password, "Admin", "Synthetic", "Admin", null));
        await h.Login(Email("admin"), password, other: true);
        await Denied(Body(ownProperty), HttpStatusCode.Forbidden, other: true);                // Admin without a Company profile
        Console.WriteLine("PASS M3.19 anonymous/Edge 401, other Company/foreign property/missing request 404, invalid body 400, Technician/Admin 403; no state, assignment or matching change");

        // 3. Two competing attachments from the same Company: the Status concurrency token lets only one save.
        gate.Arm(requestId, ownProperty);
        HttpResponseMessage[] race;
        async Task<HttpResponseMessage> Winner()
        {
            try { return await h.Send(HttpMethod.Post, path, Body(ownProperty)); }
            finally { gate.ReleaseLoser(); }
        }
        try { race = await Task.WhenAll(Winner(), h.Send(HttpMethod.Post, path, Body(secondOwnProperty))); }
        finally { gate.Disarm(); }
        using (var winner = race[0]) using (var loser = race[1])
        {
            Assert.Equal(2, gate.Arrivals);
            Assert.Equal(HttpStatusCode.OK, winner.StatusCode); Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);
            var body = await winner.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Assigned", body.GetProperty("matching").GetString()); Assert.Equal("Assigned", body.GetProperty("status").GetString());
            Assert.Equal(ownProperty, body.GetProperty("propertyId").GetString());
        }
        using (var fresh = h.Scope())
        {
            var ctx = fresh.ServiceProvider.GetRequiredService<AppDbContext>();
            var r = await ctx.Set<ServiceRequest>().AsNoTracking().SingleAsync(x => x.RequestId == RequestId.From(requestId));
            Assert.Equal(ERequestStatus.Assigned, r.Status); Assert.Equal(ownProperty, r.PropertyId!.Value); Assert.Equal(EServiceCategory.Installation, r.RequestedCategory);
            Assert.Equal(ClientIdentity.FromCompany(company), r.Client); Assert.Equal(technician, r.SelectedTechnicianId!.Value);
            Assert.Equal(0.01, (double)r.Geolocation!.Latitude, 6);
            // Projection only: materializing ServiceAssignment fails on its jsonb value objects (pre-existing, see M3.19 report).
            var assignment = await ctx.Set<ServiceAssignment>().AsNoTracking().Where(a => a.RequestId == RequestId.From(requestId))
                .Select(a => new { a.Status, a.TechnicianId }).SingleAsync();
            Assert.Equal(EAssignmentStatus.Assigned, assignment.Status); Assert.Equal(technician, assignment.TechnicianId!.Value);
            Assert.True(await fresh.ServiceProvider.GetRequiredService<IInstallationAssignmentReader>()
                .IsAssignedInstallation(requestId, ownProperty, technician, ClientIdentity.FromCompany(company)));
        }
        Assert.Equal(1, counters.AiCalls);
        Console.WriteLine("PASS M3.19 competing attachments 200/409: one transition, one matching (AI provider blocked, deterministic fallback), one assignment; reader accepts the relation");

        // 4. Repetition after success: 409, no overwrite, no new matching.
        await Denied(Body(secondOwnProperty), HttpStatusCode.Conflict);
        Console.WriteLine("PASS M3.19 repeated attachment 409 without overwrite or matching");

        // 5. Assigned technician selects own stock explicitly and installs it (M3.14–M3.16 policies unchanged).
        await h.Login(Email("tech"), password, other: true);
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string deviceId;
        using (var registered = await h.Send(HttpMethod.Post, "/api/v1/iot-devices", new { serialNumber = "SYNTHETIC-M319-" + task, plainTextApiKey = key, firmwareVersion = "synthetic-1" }, otherUser: true))
        {
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
            deviceId = (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        }
        using (var denied = await h.Send(HttpMethod.Post, "/api/v1/io-t-devices/" + deviceId + "/assign/" + ownProperty, new { installationRequestId = requestId }))
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);                          // the Company cannot assign
        using (var assigned = await h.Send(HttpMethod.Post, "/api/v1/io-t-devices/" + deviceId + "/assign/" + ownProperty, new { installationRequestId = requestId }, otherUser: true))
            Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        using (var installed = await h.Send(HttpMethod.Put, "/api/v1/io-t-devices/" + deviceId + "/install/" + ownProperty,
            new { technicianId = technician, firmwareVersion = "synthetic-1", installedAt = DateTime.UtcNow.AddMinutes(-1) }, otherUser: true))
            Assert.Equal(HttpStatusCode.OK, installed.StatusCode);
        Console.WriteLine("PASS M3.19 assigned technician assigns and installs own stock on the Company property via HTTP");

        // 6. No candidates: the preparation stays persisted and the failure is reported, not hidden.
        var farRequest = await CreateRequest("sub-synthetic-m319-far-" + task);
        using (var noCandidates = await h.Send(HttpMethod.Post, "/api/v1/service-requests/" + farRequest + "/installation-property", Body(farProperty)))
        {
            Assert.Equal(HttpStatusCode.OK, noCandidates.StatusCode);
            var body = await noCandidates.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("NoCandidates", body.GetProperty("matching").GetString()); Assert.Equal("PendingAssignment", body.GetProperty("status").GetString());
        }
        using (var fresh = h.Scope())
        {
            var ctx = fresh.ServiceProvider.GetRequiredService<AppDbContext>();
            var r = await ctx.Set<ServiceRequest>().AsNoTracking().SingleAsync(x => x.RequestId == RequestId.From(farRequest));
            Assert.Equal(ERequestStatus.PendingAssignment, r.Status); Assert.Equal(farProperty, r.PropertyId!.Value); Assert.Null(r.SelectedTechnicianId);
            var failed = await ctx.Set<ServiceAssignment>().AsNoTracking().Where(a => a.RequestId == RequestId.From(farRequest))
                .Select(a => new { a.Status }).SingleAsync();
            Assert.Equal(EAssignmentStatus.Failed, failed.Status);
        }
        Assert.Equal(1, counters.AiCalls);
        Console.WriteLine("PASS M3.19 no candidates: request persisted PendingAssignment with the property and a Failed assignment record; reported as NoCandidates");

        Assert.Equal(0, counters.LegacyOrQuota); Assert.Equal(0, h.ForbiddenCalls);
        Console.WriteLine("PASS M3.19 no request-quota/legacy flow and no outbound HTTP; AI provider sentinel invoked once and blocked");
    }

    // Prints only PostgreSQL SQLSTATE and schema object names (no values, connection or provider text), then rethrows.
    private static async Task Diagnosed(Func<Task> save, string step)
    {
        try { await save(); }
        catch (DbUpdateException ex) when (ex.GetBaseException() is PostgresException pg)
        {
            Console.Error.WriteLine("DIAG M3.19 " + step + ": sqlstate=" + pg.SqlState + " table=" + pg.TableName + " column=" + pg.ColumnName + " constraint=" + pg.ConstraintName);
            throw;
        }
        catch (DbUpdateException ex)
        {
            // Non-provider EF failure: type chain and the entity types involved only.
            Console.Error.WriteLine("DIAG M3.19 " + step + ": " + ex.GetType().Name + " <- " + ex.GetBaseException().GetType().Name
                + " entries=" + string.Join(",", ex.Entries.Select(e => e.Metadata.ClrType.Name + ":" + e.State)));
            throw;
        }
    }

    private static T AssignmentHttpFixtureProxy<T>(Func<MethodInfo, object?[]?, object?> call) where T : class
        => Assets.AssignmentHttpFixture.Proxy<T>(call);

    private sealed class Counters
    {
        internal int AiCalls; internal int LegacyOrQuota;
        internal T Block<T>(Action count) where T : class
            => AssignmentHttpFixtureProxy<T>((mi, a) => { count(); throw new InvalidOperationException("Blocked in isolated demo: " + mi.Name); });
    }
}

// Holds both competing PendingInstallation → PendingAssignment saves until each has read the same status.
internal sealed class AttachRaceGate : SaveChangesInterceptor
{
    private TaskCompletionSource<bool> both = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<bool> releaseLoser = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? requestId, winnerProperty;
    private int armed, arrivals;
    internal int Arrivals => Volatile.Read(ref arrivals);
    internal void Arm(string request, string winner) { requestId = request; winnerProperty = winner; Volatile.Write(ref armed, 1); }
    internal void ReleaseLoser() => releaseLoser.TrySetResult(true);
    internal void Disarm() { Volatile.Write(ref armed, 0); ReleaseLoser(); }
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref armed) == 0 || eventData.Context == null) return result;
        var entry = eventData.Context.ChangeTracker.Entries<ServiceRequest>().SingleOrDefault(e => e.State == EntityState.Modified
            && e.Entity.RequestId.Value == requestId && e.Entity.Status == ERequestStatus.PendingAssignment
            && e.Property(r => r.Status).OriginalValue == ERequestStatus.PendingInstallation);
        if (entry == null) return result;
        var count = Interlocked.Increment(ref arrivals); Assert.InRange(count, 1, 2); if (count == 2) both.TrySetResult(true);
        await both.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        if (entry.Entity.PropertyId!.Value != winnerProperty) await releaseLoser.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        return result;
    }
}
