using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Commands;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

// M3.28: sign-in over real IAM/JWT and EF/Npgsql. Suspension and reactivation use the real IAM command service
// (there is no HTTP endpoint for them).
public static class M328InactiveSignInDemo
{
    public static async Task Run(string manifestPath)
    {
        var connection = await M319SubscriptionInstallationDemo.Connect(manifestPath);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath)); var m = json.RootElement;
        var task = m.GetProperty("taskId").GetString()!;
        using var h = new RealBackendScenario(connection, m.GetProperty("jwtSecret").GetString()!, m.GetProperty("edgeKey").GetString()!, "unused-synthetic-m328", machineScope: false);
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + "aA1!";
        var email = "m328-owner-" + task + "@example.invalid";
        await h.RegisterAndComplete(email, password, "00000001");
        var userId = h.Owner.Id.Value;
        async Task<HttpResponseMessage> SignIn(string pw) => await h.Send(HttpMethod.Post, "/api/v1/authentication/sign-in", new { email, password = pw }, anonymous: true);
        // A token issued while active stops working once the account is suspended (M3.27 middleware).
        using (var me = await h.Send(HttpMethod.Get, "/api/v1/profiles/me")) Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using (var scope = h.Scope())
            await scope.ServiceProvider.GetRequiredService<IUserCommandService>().Handle(new SuspendUserAccountCommand(userId, "Synthetic M3.28"));
        using (var me = await h.Send(HttpMethod.Get, "/api/v1/profiles/me")) Assert.Equal(HttpStatusCode.Forbidden, me.StatusCode);
        using (var r = await SignIn(password))
        {
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            var body = await r.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Account is not active", body.GetProperty("message").GetString()); Assert.False(body.TryGetProperty("token", out _));
        }
        using (var r = await SignIn("wrong-" + password))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
            Assert.DoesNotContain("not active", await r.Content.ReadAsStringAsync());
        }
        using (var scope = h.Scope())
            await scope.ServiceProvider.GetRequiredService<IUserCommandService>().Handle(new ActivateUserAccountCommand(userId));
        using (var r = await SignIn(password)) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        await h.Login(email, password);
        using (var me = await h.Send(HttpMethod.Get, "/api/v1/profiles/me")) Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(0, h.ForbiddenCalls);
        Console.WriteLine("PASS M3.28 real IAM: suspended account gets 403 on sign-in (no token) and its earlier token 403; wrong password stays 401; reactivated account signs in and works");
    }
}
