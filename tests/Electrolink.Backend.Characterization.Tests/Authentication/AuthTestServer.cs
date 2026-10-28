using System.Security.Claims;
using System.Text;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Queries;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Components;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Tokens.JWT.Configuration;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Tokens.JWT.Services;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Processing.Domain.Services;
using Hampcoders.Electrolink.API.Processing.Interfaces.REST;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Electrolink.Backend.Characterization.Tests.Authentication;

// No Program/WebApplicationFactory: no production configuration, DB, jobs or network listeners.
internal sealed class AuthTestServer : IDisposable
{
    internal const string EdgeKey = "synthetic-m02-edge-key";
    private const string Secret = "synthetic-m02-signing-key-only-for-tests-at-least-32-bytes";
    internal const string HistoryPath = "/api/v1/iot/devices/dev-m02/relay/history";
    internal User Human { get; }
    internal TokenService Tokens { get; }
    internal HistoryQueryStub History { get; } = new();
    private readonly TestServer server;
    private readonly HttpClient client;

    internal AuthTestServer(AccessRole role = AccessRole.Admin, bool userExists = true, string? edgeKey = EdgeKey, IIoTMonitoringQueryService? monitoringQueries = null, IEnumerable<string?>? historyAllowedDevices = null, bool configureHistoryScope = true)
    {
        Human = User.Create(Email.From("m02@example.invalid"), HashedPassword.FromHash("synthetic-unused-hash"), role);
        Tokens = new TokenService(Options.Create(new TokenSettings { Secret = Secret }), NullLogger<TokenService>.Instance);
        server = new TestServer(new WebHostBuilder()
            .ConfigureAppConfiguration((_, config) =>
            {
                config.Sources.Clear();
                var values = new Dictionary<string, string?> { ["EdgeApi:ApiKey"] = edgeKey };
                if (configureHistoryScope)
                {
                    var index = 0;
                    foreach (var deviceId in historyAllowedDevices ?? new[] { "dev-m02" })
                        values[$"EdgeApi:RelayHistory:AllowedDeviceIds:{index++}"] = deviceId;
                }
                config.AddInMemoryCollection(values);
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton<ITokenService>(Tokens);
                services.AddSingleton<IUserQueryService>(new UserQueryStub(userExists ? Human : null));
                services.AddRouting(options => options.LowercaseUrls = true);
                services.AddControllers(options =>
                {
                    options.Conventions.Add(new KebabCaseRouteNamingConvention());
                    options.Filters.Add(new EdgeActionSentinel());
                })
                    .AddApplicationPart(typeof(AuthProbeController).Assembly)
                    .AddApplicationPart(typeof(DeviceMonitoringController).Assembly)
                    .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new AuthControllersOnly()))
                    .AddControllersAsServices();
                // Only the history action is exercised. Unexpected service access must fail, never reach infrastructure.
                services.AddTransient(_ => new DeviceMonitoringController(monitoringQueries ?? History, new UnexpectedAnomalyService()));
                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
                {
                    // Mirrors Program.cs; intentionally does not execute its composition root.
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(Secret)),
                        ValidateIssuer = false, ValidateAudience = false,
                        ValidateLifetime = true, ClockSkew = TimeSpan.Zero
                    };
                });
                services.AddAuthorization();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.Use(async (context, next) =>
                {
                    // Test-only observation made before the custom middleware can short-circuit.
                    context.Response.Headers["X-M02-Jwt-Authenticated"] = (context.User.Identity?.IsAuthenticated == true).ToString();
                    await next(context);
                });
                app.UseMiddleware<RequestAuthorizationMiddleware>();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));
        client = server.CreateClient();
    }

    internal string ValidToken() => Tokens.GenerateToken(Human);

    internal string TokenWithRole(AccessRole claimedRole) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Human.Id.Value), new Claim(ClaimTypes.Role, claimedRole.ToString()) }),
        Expires = DateTime.UtcNow.AddHours(1),
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.ASCII.GetBytes(Secret)), SecurityAlgorithms.HmacSha256)
    });

    internal string InvalidToken(string kind)
    {
        if (kind == "malformed") return "not-a-jwt";
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Human.Id.Value), new Claim(ClaimTypes.Role, Human.Role.ToString()) }),
            IssuedAt = now.AddHours(-2), NotBefore = now.AddHours(-2),
            Expires = kind == "expired" ? now.AddHours(-1) : now.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.ASCII.GetBytes(
                kind == "bad-signature" ? "another-synthetic-signing-key-at-least-32-bytes" : Secret)), SecurityAlgorithms.HmacSha256)
        });
    }

    internal Task<HttpResponseMessage> Get(string path, string? token = null, string? apiKey = null, string scheme = "Bearer") =>
        Send(HttpMethod.Get, path, token, apiKey, scheme);

    internal async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token = null, string? apiKey = null, string scheme = "Bearer", string? gatewayHint = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token != null) request.Headers.TryAddWithoutValidation("Authorization", $"{scheme} {token}");
        if (apiKey != null) request.Headers.Add("X-Api-Key", apiKey);
        if (gatewayHint != null) request.Headers.Add("X-Gateway-Id", gatewayHint);
        return await client.SendAsync(request);
    }

    public void Dispose() { client.Dispose(); server.Dispose(); }

    // Runs after the real authorization filters, before controller activation/model binding.
    // Tests real Edge endpoint access without running ingestion, demo provisioning or relay actions.
    private sealed class EdgeActionSentinel : IResourceFilter
    {
        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            if (context.ActionDescriptor is ControllerActionDescriptor action && action.ControllerTypeInfo.AsType() == typeof(EdgeIngestionController))
            {
                context.HttpContext.Response.Headers["X-M21-Edge-Authorized"] = action.ActionName;
                context.Result = new NoContentResult();
            }
        }
        public void OnResourceExecuted(ResourceExecutedContext context) { }
    }

    private sealed class AuthControllersOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in feature.Controllers.ToArray())
                if (controller.AsType() != typeof(AuthProbeController) && controller.AsType() != typeof(DeviceMonitoringController) && controller.AsType() != typeof(EdgeIngestionController))
                    feature.Controllers.Remove(controller);
        }
    }

    private sealed class UserQueryStub(User? user) : IUserQueryService
    {
        public Task<User?> Handle(GetUserByIdQuery query) => Task.FromResult(user?.Id.Value == query.UserId ? user : null);
        public Task<IEnumerable<User>> Handle(GetAllUsersQuery query) => throw new InvalidOperationException("Unexpected user listing");
        public Task<User?> Handle(GetUserByEmailQuery query) => throw new InvalidOperationException("Unexpected email lookup");
    }

    private sealed class UnexpectedAnomalyService : IAnomalyCommandService
    {
        public Task Handle(EvaluateReadingForAnomaliesCommand command) => throw new InvalidOperationException("Unexpected anomaly evaluation");
        public Task Handle(ResolveAnomalyCommand command) => throw new InvalidOperationException("Unexpected anomaly resolution");
        public Task Handle(AcknowledgeAnomalyCommand command) => throw new InvalidOperationException("Unexpected anomaly acknowledgement");
        public Task Handle(ForceResolveAnomalyCommand command) => throw new InvalidOperationException("Unexpected forced resolution");
        public Task Handle(AutoIssueRelayCommandCommand command) => throw new InvalidOperationException("Unexpected relay command");
    }

    internal sealed class HistoryQueryStub : IIoTMonitoringQueryService
    {
        internal string? RequestedDevice { get; private set; }
        public Task<IEnumerable<RelayControlCommand>> Handle(GetRelayCommandHistoryQuery query)
        {
            RequestedDevice = query.DeviceId;
            var command = RelayControlCommand.IssueBySystem(DeviceId.From("dev-m02"), PropertyId.From("prop-m02"), ERelayTarget.Open);
            command.MarkAsSent();
            command.MarkAsExecuted(); // In-memory entity only; no command service/dispatcher is registered.
            return Task.FromResult<IEnumerable<RelayControlCommand>>(new[] { command });
        }
        public Task<DeviceReadingStream?> Handle(GetDeviceStatusQuery query) => throw new InvalidOperationException("Unexpected status query");
        public Task<IEnumerable<AnomalyRecord>> Handle(GetActiveAnomaliesByPropertyQuery query) => throw new InvalidOperationException("Unexpected anomaly query");
        public Task<DeviceReadingStream?> Handle(GetRecentReadingsWindowQuery query) => throw new InvalidOperationException("Unexpected readings query");
        public Task<RelayControlCommand?> FindPendingRelayCommandByDeviceAsync(string deviceId) => throw new InvalidOperationException("Unexpected pending relay query");
    }
}
