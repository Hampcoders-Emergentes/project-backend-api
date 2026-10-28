using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Xunit;

namespace Electrolink.Backend.Characterization.Tests.Authentication;

public sealed class EndpointAccessRegressionTests
{
    [Theory]
    [InlineData("GET", "/api/v1/iot/devices/dev-m02/status")]
    [InlineData("GET", "/api/v1/iot/devices/dev-m02/readings/recent")]
    [InlineData("GET", "/api/v1/iot/devices/properties/prop-m02/anomalies")]
    [InlineData("POST", "/api/v1/iot/devices/anomalies/anom-m02/acknowledge")]
    [InlineData("POST", "/api/v1/iot/devices/anomalies/anom-m02/force-resolve")]
    public async Task HistoryNeighbors_DoNotInheritMachineAccess(string method, string path)
    {
        using var app = new AuthTestServer();
        using var response = await app.Send(new HttpMethod(method), path, apiKey: AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(app.History.RequestedDevice);
    }

    [Theory]
    [InlineData("POST", "/api/v1/iot/edge/readings", "IngestReading")]
    [InlineData("POST", "/api/v1/iot/edge/anomalies", "ReportEdgeAnomaly")]
    [InlineData("POST", "/api/v1/iot/edge/relay/acknowledge", "AcknowledgeRelay")]
    [InlineData("GET", "/api/v1/iot/edge/devices/dev-m02/thresholds", "GetDeviceThresholds")]
    public async Task RealEdgeEndpoints_RequireValidKeyEvenWithHumanJwt(string method, string path, string action)
    {
        using var app = new AuthTestServer();
        foreach (var key in new string?[] { null, "incorrect-key", AuthTestServer.EdgeKey })
        {
            using var response = await app.Send(new HttpMethod(method), path, app.ValidToken(), key);
            Assert.Equal(key == AuthTestServer.EdgeKey ? HttpStatusCode.NoContent : HttpStatusCode.Unauthorized, response.StatusCode);
            if (key == AuthTestServer.EdgeKey)
                Assert.Equal(action, Assert.Single(response.Headers.GetValues("X-M21-Edge-Authorized")));
            else
                Assert.False(response.Headers.Contains("X-M21-Edge-Authorized"));
        }
        using var edgeOnly = await app.Send(new HttpMethod(method), path, apiKey: AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.NoContent, edgeOnly.StatusCode);
        Assert.Equal(action, Assert.Single(edgeOnly.Headers.GetValues("X-M21-Edge-Authorized")));
    }

    [Theory]
    [InlineData("/__m02/plain")]
    [InlineData("/api/v1/iot/edge/not-registered")]
    [InlineData("/api/v1/iot/devices/dev-m02/relay/history/not-registered")]
    public async Task UnmarkedOrUnmatchedRoute_DoesNotAcceptEdgeKey(string path)
    {
        using var app = new AuthTestServer();
        using var response = await app.Get(path, apiKey: AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(AccessRole.Admin, HttpStatusCode.OK)]
    [InlineData(AccessRole.SuperAdmin, HttpStatusCode.OK)]
    [InlineData(AccessRole.User, HttpStatusCode.Forbidden)]
    [InlineData(AccessRole.Maker, HttpStatusCode.Forbidden)]
    [InlineData(AccessRole.FieldManager, HttpStatusCode.Forbidden)]
    public async Task MixedCredentials_PreserveHumanRoleRestrictions(AccessRole role, HttpStatusCode expected)
    {
        using var app = new AuthTestServer(role);
        using var response = await app.Get("/__m02/admin", app.ValidToken(), AuthTestServer.EdgeKey);
        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("/__m02/human")]
    [InlineData("/__m02/admin")]
    public async Task MixedCredentials_DoNotBypassMissingHumanUser(string path)
    {
        using var app = new AuthTestServer(userExists: false);
        using var response = await app.Get(path, app.ValidToken(), AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Unauthorized: User not found", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/__m02/edge-human")]
    [InlineData("/__m02/edge-admin")]
    [InlineData("/__m02/edge-framework")]
    public async Task CombinedFilters_RequireBothHumanAndEdgeCredentials(string path)
    {
        using var app = new AuthTestServer();
        using var machineOnly = await app.Get(path, apiKey: AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.Unauthorized, machineOnly.StatusCode);
        using var allowed = await app.Get(path, app.ValidToken(), AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var body = await allowed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(app.Human.Id.Value, body.GetProperty("itemUserId").GetString());
        foreach (var key in new string?[] { null, "incorrect-key" })
        {
            using var denied = await app.Get(path, app.ValidToken(), key);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }
    }
    [Fact]
    public async Task HistoryAccess_IsNotGrantedForOtherHttpMethods()
    {
        using var app = new AuthTestServer();
        using var response = await app.Send(HttpMethod.Post, AuthTestServer.HistoryPath, apiKey: AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(app.History.RequestedDevice);
    }

}
