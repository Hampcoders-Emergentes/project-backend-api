using System.Net;
using System.Net.Http.Json;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Processing.Interfaces.REST.Resources;
using Xunit;

namespace Electrolink.Backend.Characterization.Tests.Authentication;

public sealed class RelayHistoryOwnershipTests
{
    private static string HistoryPath(string deviceId) => $"/api/v1/iot/devices/{deviceId}/relay/history";

    [Fact]
    public void ExistingModel_PreservesDistinctDevicePropertyAndOwnerReferences()
    {
        var scenario = new HistoryOwnershipScenario();
        Assert.NotEqual(scenario.DeviceA.Id, scenario.DeviceB.Id);
        Assert.NotEqual(scenario.PropertyA.Id, scenario.PropertyB.Id);
        Assert.NotEqual(scenario.PropertyA.Owner, scenario.PropertyB.Owner);
        Assert.Equal(scenario.PropertyA.Id, scenario.DeviceA.AssignedPropertyId);
        Assert.Equal(scenario.PropertyB.Id, scenario.DeviceB.AssignedPropertyId);
        Assert.Equal(scenario.PropertyA.Owner, scenario.StreamA.Owner);
        Assert.Equal(scenario.PropertyB.Owner, scenario.StreamB.Owner);
        Assert.Equal(scenario.PropertyA.Id, scenario.CommandA.PropertyId);
        Assert.Equal(scenario.PropertyB.Id, scenario.CommandB.PropertyId);
        Assert.NotEqual(scenario.DeviceA.Id.Value, scenario.DeviceA.SerialNumber.Value);
        Assert.True(scenario.DeviceA.ApiKeyHash.Verify(HistoryOwnershipScenario.DeviceCredentialA));
        Assert.False(scenario.DeviceB.ApiKeyHash.Verify(HistoryOwnershipScenario.DeviceCredentialA));
    }

    // Historical machine-access observations were corrected in M2.3; M2.2.md keeps their evidence.
    // Remaining ObservedGap cases below are still limitations, not approved requirements.
    [Fact, Trait("Category", "Regression")]
    public async Task Regression_GlobalCredentialIsRestrictedToServerAllowedDevice()
    {
        var scenario = new HistoryOwnershipScenario();
        using var app = new AuthTestServer(monitoringQueries: scenario.Queries, historyAllowedDevices: new[] { scenario.DeviceA.Id.Value });
        using var allowed = await app.Get(HistoryPath(scenario.DeviceA.Id.Value), apiKey: AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var body = await allowed.Content.ReadFromJsonAsync<RelayCommandHistoryResource>();
        Assert.NotNull(body);
        Assert.Equal(scenario.CommandA.CommandId.Value, Assert.Single(body.Commands).CommandId);
        using var denied = await app.Get(HistoryPath(scenario.DeviceB.Id.Value), apiKey: AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(scenario.DeviceA.Id.Value, Assert.Single(scenario.Repository.RequestedDevices));
    }

    [Fact, Trait("Category", "Regression")]
    public async Task Regression_AllowedEmptyHistoryRemains200ButUnlistedUnknownDeviceIs403()
    {
        var scenario = new HistoryOwnershipScenario(withCommands: false);
        using var app = new AuthTestServer(monitoringQueries: scenario.Queries, historyAllowedDevices: new[] { scenario.DeviceA.Id.Value });
        using var allowed = await app.Get(HistoryPath(scenario.DeviceA.Id.Value), apiKey: AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var body = await allowed.Content.ReadFromJsonAsync<RelayCommandHistoryResource>();
        Assert.NotNull(body);
        Assert.Empty(body.Commands);
        Assert.Equal("UNKNOWN", body.CurrentRelayState);
        Assert.Null(body.LastCommandAt);
        using var denied = await app.Get(HistoryPath("dev-m22-unknown"), apiKey: AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(scenario.DeviceA.Id.Value, Assert.Single(scenario.Repository.RequestedDevices));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InvalidGlobalCredential_IsRejectedBeforeDeviceQuery(int selection)
    {
        var scenario = new HistoryOwnershipScenario();
        using var app = new AuthTestServer(monitoringQueries: scenario.Queries);
        var deviceId = selection switch { 0 => scenario.DeviceA.Id.Value, 1 => scenario.DeviceB.Id.Value, _ => "dev-m22-unknown" };
        using var response = await app.Get(HistoryPath(deviceId), apiKey: "synthetic-invalid");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(scenario.Repository.RequestedDevices);
    }

    [Theory, Trait("Category", "ObservedGap")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObservedGap_RegisteredDeviceCredentialIsNotAcceptedAsGlobalCredential(bool secondDevice)
    {
        var scenario = new HistoryOwnershipScenario();
        using var app = new AuthTestServer(monitoringQueries: scenario.Queries);
        var deviceId = secondDevice ? scenario.DeviceB.Id.Value : scenario.DeviceA.Id.Value;
        var deviceCredential = secondDevice ? HistoryOwnershipScenario.DeviceCredentialB : HistoryOwnershipScenario.DeviceCredentialA;
        var registeredDevice = secondDevice ? scenario.DeviceB : scenario.DeviceA;
        Assert.True(registeredDevice.ApiKeyHash.Verify(deviceCredential));
        using var response = await app.Get(HistoryPath(deviceId), apiKey: deviceCredential);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(scenario.Repository.RequestedDevices);
    }

    [Fact, Trait("Category", "ObservedGap")]
    public async Task ObservedGap_HumanUserCanReadBothDevicesWithoutOwnerScopeInQuery()
    {
        var scenario = new HistoryOwnershipScenario();
        using var app = new AuthTestServer(AccessRole.User, monitoringQueries: scenario.Queries);
        foreach (var command in new[] { scenario.CommandA, scenario.CommandB })
        {
            using var response = await app.Get(HistoryPath(command.DeviceId.Value), app.ValidToken());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<RelayCommandHistoryResource>();
            Assert.NotNull(body);
            Assert.Equal(command.CommandId.Value, Assert.Single(body.Commands).CommandId);
        }
        Assert.Equal(2, scenario.Repository.RequestedDevices.Count);
    }

    [Theory, Trait("Category", "Regression")]
    [InlineData("client-claimed-a")]
    [InlineData("client-claimed-b")]
    public async Task Regression_ClientGatewayHintCannotChangeServerDeviceScope(string hint)
    {
        var scenario = new HistoryOwnershipScenario();
        using var app = new AuthTestServer(monitoringQueries: scenario.Queries, historyAllowedDevices: new[] { scenario.DeviceA.Id.Value });
        foreach (var deviceId in new[] { scenario.DeviceA.Id.Value, scenario.DeviceB.Id.Value })
        {
            using var response = await app.Send(HttpMethod.Get, HistoryPath(deviceId) + "?gatewayId=" + hint,
                apiKey: AuthTestServer.EdgeKey, gatewayHint: hint);
            Assert.Equal(deviceId == scenario.DeviceA.Id.Value ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        }
        using var invalid = await app.Send(HttpMethod.Get, HistoryPath(scenario.DeviceA.Id.Value),
            apiKey: "synthetic-invalid", gatewayHint: hint);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        Assert.Equal(scenario.DeviceA.Id.Value, Assert.Single(scenario.Repository.RequestedDevices));
    }

    [Fact]
    public async Task SerialIsNotAnImplicitAliasForAllowedInternalDeviceId()
    {
        var scenario = new HistoryOwnershipScenario();
        using var app = new AuthTestServer(monitoringQueries: scenario.Queries, historyAllowedDevices: new[] { scenario.DeviceA.Id.Value });
        using var bySerial = await app.Get(HistoryPath(scenario.DeviceA.SerialNumber.Value), apiKey: AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.Forbidden, bySerial.StatusCode);
        Assert.Empty(scenario.Repository.RequestedDevices);
    }
}
