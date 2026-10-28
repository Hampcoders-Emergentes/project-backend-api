using System.Net;
using System.Net.Http.Json;
using Hampcoders.Electrolink.API.Processing.Interfaces.REST.Resources;
using Xunit;

namespace Electrolink.Backend.Characterization.Tests.Authentication;

public sealed class RelayHistoryScopeTests
{
    private static string PathFor(string id) => $"/api/v1/iot/devices/{id}/relay/history";

    [Fact]
    public async Task ServerAllowedDevice_ReachesRealQueryServiceAndReturnsItsHistory()
    {
        var scenario = new HistoryOwnershipScenario();
        using var app = new AuthTestServer(monitoringQueries: scenario.Queries, historyAllowedDevices: new[] { scenario.DeviceA.Id.Value });
        using var response = await app.Get(PathFor(scenario.DeviceA.Id.Value), apiKey: AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RelayCommandHistoryResource>();
        Assert.NotNull(body);
        Assert.Equal(scenario.CommandA.CommandId.Value, Assert.Single(body.Commands).CommandId);
        Assert.Equal(scenario.DeviceA.Id.Value, Assert.Single(scenario.Repository.RequestedDevices));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MachineScopeDenial_DoesNotFallBackToAValidHumanJwt(bool includeHuman)
    {
        var scenario = new HistoryOwnershipScenario();
        using var app = new AuthTestServer(monitoringQueries: scenario.Queries, historyAllowedDevices: new[] { scenario.DeviceA.Id.Value });
        using var response = await app.Get(PathFor(scenario.DeviceB.Id.Value), includeHuman ? app.ValidToken() : null, AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(scenario.Repository.RequestedDevices);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrEmptyList_DeniesMachineBeforeRepository(bool missing)
    {
        var scenario = new HistoryOwnershipScenario();
        using var app = new AuthTestServer(monitoringQueries: scenario.Queries, historyAllowedDevices: Array.Empty<string>(), configureHistoryScope: !missing);
        using var response = await app.Get(PathFor(scenario.DeviceA.Id.Value), apiKey: AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(scenario.Repository.RequestedDevices);
    }

    [Theory]
    [InlineData("case")]
    [InlineData("prefix")]
    [InlineData("whitespace")]
    [InlineData("blank")]
    [InlineData("null")]
    public async Task ListMatchesExactIdsWithoutNormalizationOrWildcard(string variant)
    {
        var scenario = new HistoryOwnershipScenario();
        string? entry = variant switch
        {
            "case" => scenario.DeviceA.Id.Value.ToUpperInvariant(),
            "prefix" => "dev-*",
            "whitespace" => " " + scenario.DeviceA.Id.Value + " ",
            "blank" => " ",
            _ => null
        };
        using var app = new AuthTestServer(monitoringQueries: scenario.Queries, historyAllowedDevices: new[] { entry });
        using var response = await app.Get(PathFor(scenario.DeviceA.Id.Value), apiKey: AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(scenario.Repository.RequestedDevices);
    }

    [Fact]
    public async Task AllowedDeviceWithNoCommands_Preserves200Empty()
    {
        var scenario = new HistoryOwnershipScenario(withCommands: false);
        using var app = new AuthTestServer(monitoringQueries: scenario.Queries, historyAllowedDevices: new[] { scenario.DeviceA.Id.Value });
        using var response = await app.Get(PathFor(scenario.DeviceA.Id.Value), apiKey: AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RelayCommandHistoryResource>();
        Assert.NotNull(body);
        Assert.Empty(body.Commands);
        Assert.Equal("UNKNOWN", body.CurrentRelayState);
        Assert.Null(body.LastCommandAt);
        Assert.Single(scenario.Repository.RequestedDevices);
    }

    [Fact]
    public async Task ExplicitListEntry_DoesNotImplyDeviceRegistrationOrInstallation()
    {
        var scenario = new HistoryOwnershipScenario();
        const string requested = "dev-m23-not-in-fixture-inventory";
        using var app = new AuthTestServer(monitoringQueries: scenario.Queries, historyAllowedDevices: new[] { requested });
        using var response = await app.Get(PathFor(requested), apiKey: AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RelayCommandHistoryResource>();
        Assert.NotNull(body);
        Assert.Empty(body.Commands);
        Assert.Equal(requested, Assert.Single(scenario.Repository.RequestedDevices));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HumanChannel_DoesNotRequireListOrTrustAnInvalidEdgeHeader(bool invalidEdgeHeader)
    {
        var scenario = new HistoryOwnershipScenario();
        using var app = new AuthTestServer(monitoringQueries: scenario.Queries, configureHistoryScope: false);
        using var response = await app.Get(PathFor(scenario.DeviceB.Id.Value), app.ValidToken(), invalidEdgeHeader ? "synthetic-invalid" : null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(scenario.DeviceB.Id.Value, Assert.Single(scenario.Repository.RequestedDevices));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("synthetic-invalid")]
    public async Task InvalidOrMissingCredential_Remains401BeforeScopeAndQuery(string? invalidCredential)
    {
        var scenario = new HistoryOwnershipScenario();
        using var app = new AuthTestServer(monitoringQueries: scenario.Queries, historyAllowedDevices: new[] { scenario.DeviceA.Id.Value });
        using var response = await app.Get(PathFor(scenario.DeviceA.Id.Value), apiKey: invalidCredential);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(scenario.Repository.RequestedDevices);
    }

    [Fact]
    public async Task ClientHintsCannotPopulateTheServerListOrForceHumanFallback()
    {
        var scenario = new HistoryOwnershipScenario();
        using var app = new AuthTestServer(monitoringQueries: scenario.Queries, configureHistoryScope: false);
        using var response = await app.Send(HttpMethod.Get, PathFor(scenario.DeviceA.Id.Value) + "?AllowedDeviceIds=" + scenario.DeviceA.Id.Value + "&channel=human",
            app.ValidToken(), AuthTestServer.EdgeKey, gatewayHint: "client-claimed-human");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(scenario.Repository.RequestedDevices);
    }

    [Fact]
    public async Task MachinePrecedenceIsPreservedWithInvalidJwtAndAllowedResource()
    {
        var scenario = new HistoryOwnershipScenario();
        using var app = new AuthTestServer(monitoringQueries: scenario.Queries, historyAllowedDevices: new[] { scenario.DeviceA.Id.Value });
        using var response = await app.Get(PathFor(scenario.DeviceA.Id.Value), app.InvalidToken("bad-signature"), AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(scenario.Repository.RequestedDevices);
    }

    [Theory]
    [InlineData("POST", "/api/v1/iot/edge/readings")]
    [InlineData("POST", "/api/v1/iot/edge/anomalies")]
    [InlineData("POST", "/api/v1/iot/edge/relay/acknowledge")]
    [InlineData("GET", "/api/v1/iot/edge/devices/dev-m23/thresholds")]
    public async Task OtherEdgeActionsAreNotSubjectToHistoryList(string method, string path)
    {
        using var app = new AuthTestServer(configureHistoryScope: false);
        using var response = await app.Send(new HttpMethod(method), path, apiKey: AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.Contains("X-M21-Edge-Authorized"));
    }
}
