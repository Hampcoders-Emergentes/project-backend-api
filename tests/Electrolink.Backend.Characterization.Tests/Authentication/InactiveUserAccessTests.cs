using System.Net;
using System.Reflection;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.Authentication;

// M3.27: RequestAuthorizationMiddleware rejects a valid JWT whose IAM user is not Active (Suspended or PendingVerification)
// with 403 before any human endpoint runs. Anonymous endpoints and the explicit Edge channel are unaffected.
public sealed class InactiveUserAccessTests
{
    static void SetStatus(User user, EUserStatus status)
    {
        if (status == EUserStatus.Suspended) { user.Suspend("synthetic"); return; }
        // No production flow creates PendingVerification today; set it directly to cover the rule.
        typeof(User).GetProperty(nameof(User.Status))!.SetValue(user, status);
    }

    [Theory]
    [InlineData(EUserStatus.Suspended, "/__m02/plain")]
    [InlineData(EUserStatus.Suspended, "/__m02/human")]
    [InlineData(EUserStatus.Suspended, "/__m02/admin")]
    [InlineData(EUserStatus.Suspended, "/__m02/framework")]
    [InlineData(EUserStatus.PendingVerification, "/__m02/human")]
    [InlineData(EUserStatus.PendingVerification, "/__m02/admin")]
    public async Task Inactive_user_with_a_valid_token_is_403_before_the_endpoint(EUserStatus status, string path)
    {
        using var app = new AuthTestServer(AccessRole.Admin);
        var token = app.ValidToken();
        SetStatus(app.Human, status);
        using var response = await app.Get(path, token);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Forbidden: Account is not active", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Inactive_user_does_not_pass_a_human_endpoint_by_adding_the_edge_key()
    {
        using var app = new AuthTestServer(AccessRole.Admin);
        var token = app.ValidToken();
        SetStatus(app.Human, EUserStatus.Suspended);
        using var response = await app.Get("/__m02/edge-human", token, AuthTestServer.EdgeKey);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/__m02/human")]
    [InlineData("/__m02/admin")]
    public async Task Active_user_is_unchanged(string path)
    {
        using var app = new AuthTestServer(AccessRole.Admin);
        using var response = await app.Get(path, app.ValidToken());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_endpoint_and_edge_only_channel_do_not_load_or_check_a_user()
    {
        using var app = new AuthTestServer(AccessRole.Admin);
        var token = app.ValidToken();
        SetStatus(app.Human, EUserStatus.Suspended);
        using (var anonymous = await app.Get("/__m02/anonymous", token)) Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
        using (var edge = await app.Get("/__m02/edge", apiKey: AuthTestServer.EdgeKey)) Assert.Equal(HttpStatusCode.OK, edge.StatusCode);
    }
}
