using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Processing.Interfaces.REST.Resources;
using Xunit;
using System.Reflection;
using Hampcoders.Electrolink.API.Processing.Interfaces.REST;
using Hampcoders.Electrolink.API.Processing.Infrastructure.Pipeline.Middleware.Attributes;

namespace Electrolink.Backend.Characterization.Tests.Authentication;

public sealed class JwtAndEdgeAccessTests
{
    [Theory]
    [InlineData("/__m02/plain")]
    [InlineData("/__m02/human")]
    [InlineData("/__m02/admin")]
    [InlineData(AuthTestServer.HistoryPath)]
    public async Task NoCredentials_Returns401BeforeAction(string path)
    {
        using var app = new AuthTestServer();
        using var response = await app.Get(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Unauthorized: Missing token", await response.Content.ReadAsStringAsync());
        Assert.Null(app.History.RequestedDevice);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("expired")]
    [InlineData("bad-signature")]
    public async Task InvalidJwt_Returns401WithoutLoadingHistory(string kind)
    {
        using var app = new AuthTestServer();
        using var response = await app.Get(AuthTestServer.HistoryPath, app.InvalidToken(kind));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Unauthorized: Invalid token", await response.Content.ReadAsStringAsync());
        Assert.Equal("False", Assert.Single(response.Headers.GetValues("X-M02-Jwt-Authenticated")));
        Assert.Null(app.History.RequestedDevice);
    }

    [Fact]
    public async Task ValidHuman_PopulatesBothPrincipalAndCustomItems()
    {
        using var app = new AuthTestServer();
        using var response = await app.Get("/__m02/human", app.ValidToken());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("authenticated").GetBoolean());
        Assert.Equal(app.Human.Id.Value, body.GetProperty("subject").GetString());
        Assert.Equal(app.Human.Id.Value, body.GetProperty("itemUserId").GetString());
        Assert.Equal("Admin", body.GetProperty("itemRole").GetString());
    }

    [Theory]
    [InlineData(AccessRole.Admin, HttpStatusCode.OK)]
    [InlineData(AccessRole.SuperAdmin, HttpStatusCode.OK)]
    [InlineData(AccessRole.User, HttpStatusCode.Forbidden)]
    [InlineData(AccessRole.Maker, HttpStatusCode.Forbidden)]
    [InlineData(AccessRole.FieldManager, HttpStatusCode.Forbidden)]
    public async Task HumanRoles_AreCheckedByCustomFilter(AccessRole role, HttpStatusCode expected)
    {
        using var app = new AuthTestServer(role);
        using var response = await app.Get("/__m02/admin", app.ValidToken());
        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(AccessRole.User, AccessRole.Admin, HttpStatusCode.Forbidden)]
    [InlineData(AccessRole.Admin, AccessRole.User, HttpStatusCode.OK)]
    public async Task CustomRoleFilter_UsesLoadedUserRoleRatherThanJwtRole(AccessRole storedRole, AccessRole claimedRole, HttpStatusCode expected)
    {
        using var app = new AuthTestServer(storedRole);
        using var response = await app.Get("/__m02/admin", app.TokenWithRole(claimedRole));
        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(nameof(EdgeIngestionController.IngestReading))]
    [InlineData(nameof(EdgeIngestionController.ReportEdgeAnomaly))]
    [InlineData(nameof(EdgeIngestionController.AcknowledgeRelay))]
    [InlineData(nameof(EdgeIngestionController.GetDeviceThresholds))]
    public void RealEdgeActions_KeepEdgeCredentialMetadata(string action)
    {
        var controller = typeof(EdgeIngestionController);
        Assert.NotNull(controller.GetMethod(action));
        Assert.NotNull(controller.GetCustomAttribute<RequireEdgeApiKeyAttribute>());
    }

    [Theory]
    [InlineData("/__m02/edge", HttpStatusCode.OK)]
    [InlineData("/__m02/human", HttpStatusCode.Unauthorized)]
    public async Task ValidEdgeKey_WithInvalidJwtDoesNotBecomeHuman(string path, HttpStatusCode expected)
    {
        using var app = new AuthTestServer();
        using var response = await app.Get(path, app.InvalidToken("bad-signature"), AuthTestServer.EdgeKey);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("False", Assert.Single(response.Headers.GetValues("X-M02-Jwt-Authenticated")));
    }

    [Fact]
    public async Task HumanAccess_WithWrongEdgeHeaderStillUsesJwt()
    {
        using var app = new AuthTestServer();
        using var response = await app.Get("/__m02/human", app.ValidToken(), "incorrect-key");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SignedTokenWithoutExistingUser_Returns401()
    {
        using var app = new AuthTestServer(userExists: false);
        using var response = await app.Get("/__m02/human", app.ValidToken());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Unauthorized: User not found", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("incorrect-key")]
    public async Task EdgeFilter_MissingOrWrongKeyRejectsEvenValidHuman(string? apiKey)
    {
        using var app = new AuthTestServer();
        using var response = await app.Get("/__m02/edge", app.ValidToken(), apiKey);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Unauthorized: invalid or missing API key.", body.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("incorrect-key")]
    public async Task EdgeRequest_WithoutHumanOrValidKeyStopsInMiddleware(string? apiKey)
    {
        using var app = new AuthTestServer();
        using var response = await app.Get("/__m02/edge", apiKey: apiKey);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Unauthorized: Missing token", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ValidEdgeKey_PassesEdgeFilterWithoutCreatingHumanIdentity()
    {
        using var app = new AuthTestServer();
        using var response = await app.Get("/__m02/edge", apiKey: AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("authenticated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("subject").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("itemUserId").ValueKind);
    }

    [Fact]
    public async Task UnconfiguredEdgeKey_WithHumanTokenFailsClosedWith500()
    {
        using var app = new AuthTestServer(edgeKey: null);
        using var response = await app.Get("/__m02/edge", app.ValidToken(), AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Theory]
    [InlineData("/__m02/human")]
    [InlineData("/__m02/admin")]
    [InlineData("/__m02/edge-human")]
    [InlineData("/__m02/edge-admin")]
    [InlineData("/__m02/framework")]
    public async Task EdgeKeyAlone_DoesNotSatisfyHumanFilters(string path)
    {
        using var app = new AuthTestServer();
        using var response = await app.Get(path, apiKey: AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RelayHistory_ValidHumanOrEdgePreservesRouteAndResponse(bool useEdge)
    {
        using var app = new AuthTestServer();
        using var response = await app.Get(AuthTestServer.HistoryPath,
            useEdge ? null : app.ValidToken(), useEdge ? AuthTestServer.EdgeKey : null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var history = await response.Content.ReadFromJsonAsync<RelayCommandHistoryResource>();
        Assert.NotNull(history);
        Assert.Equal("dev-m02", app.History.RequestedDevice);
        Assert.Equal("dev-m02", history.DeviceId);
        Assert.Equal("Open", history.CurrentRelayState);
        Assert.NotNull(history.LastCommandAt);
        var command = Assert.Single(history.Commands);
        Assert.Equal("Executed", command.Status);
        Assert.Equal("SYSTEM", command.RequestedBy);
        Assert.Equal("CRITICAL_ANOMALY_AUTO", command.AuthorizationSource);
        Assert.NotNull(command.ExecutedAt);
    }

    [Fact]
    public async Task RelayHistory_WrongEdgeKeyDoesNotReachQuery()
    {
        using var app = new AuthTestServer();
        using var response = await app.Get(AuthTestServer.HistoryPath, apiKey: "incorrect-key");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(app.History.RequestedDevice);
    }

    [Fact]
    public async Task CustomAnonymousMetadata_BypassesMiddlewareAndHumanFilters()
    {
        using var app = new AuthTestServer();
        using var response = await app.Get("/__m02/anonymous");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Regressions replacing the historical gap observations; M0.2.md preserves the original evidence.
    [Fact, Trait("Category", "Regression")]
    public async Task Regression_EdgeKeyDoesNotBypassUnmarkedRouteWithoutHuman()
    {
        using var app = new AuthTestServer();
        using var response = await app.Get("/__m02/plain", apiKey: AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Unauthorized: Missing token", await response.Content.ReadAsStringAsync());
    }

    [Fact, Trait("Category", "Regression")]
    public async Task Regression_EdgeKeyWithValidHumanPreservesBothIdentityRepresentations()
    {
        using var app = new AuthTestServer();
        using var response = await app.Get("/__m02/plain", app.ValidToken(), AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("authenticated").GetBoolean());
        Assert.Equal(app.Human.Id.Value, body.GetProperty("subject").GetString());
        Assert.Equal(app.Human.Id.Value, body.GetProperty("itemUserId").GetString());
        Assert.Equal("Admin", body.GetProperty("itemRole").GetString());
        using var custom = await app.Get("/__m02/human", app.ValidToken(), AuthTestServer.EdgeKey);
        using var framework = await app.Get("/__m02/framework", app.ValidToken(), AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.OK, custom.StatusCode);
        Assert.Equal(HttpStatusCode.OK, framework.StatusCode);
    }

    [Fact, Trait("Category", "SecurityTarget")]
    public async Task SecurityTarget_EdgeKeyMustNotAuthorizeAnUnmarkedHumanRoute()
    {
        using var app = new AuthTestServer();
        using var response = await app.Get("/__m02/plain", apiKey: AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory, Trait("Category", "SecurityTarget")]
    [InlineData("/__m02/human")]
    [InlineData("/__m02/admin")]
    public async Task SecurityTarget_ValidHumanMustNotLoseAccessWhenEdgeHeaderIsPresent(string path)
    {
        using var app = new AuthTestServer();
        using var response = await app.Get(path, app.ValidToken(), AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
