using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Electrolink.Backend.Characterization.Tests.Assets;
using Hampcoders.Electrolink.API.IAM.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.IAM.Domain.Repositories;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.IAM.Interfaces.REST;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using IamProfiles = Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices.ExternalProfilesService;
namespace Electrolink.Backend.Characterization.Tests.Authentication;

// M3.28: sign-in and refresh-claims issue no token to a Suspended/PendingVerification account. The password is verified first,
// so a wrong password stays the generic 401 and the account status is only revealed with valid credentials.
public sealed class InactiveSignInTests
{
    [Fact]
    public async Task Active_account_signs_in_and_refreshes_as_before()
    {
        using var h = new SignInFixture();
        using (var r = await h.SignIn("active", SignInFixture.Password))
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal("synthetic-token", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        }
        using (var r = await h.Refresh("active")) Assert.True(r.StatusCode == HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        Assert.Equal(2, h.Tokens);
    }

    [Theory]
    [InlineData("suspended")]
    [InlineData("pending")]
    public async Task Inactive_account_with_the_right_password_is_403_without_token_or_sign_in_event(string who)
    {
        using var h = new SignInFixture();
        using var r = await h.SignIn(who, SignInFixture.Password);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("{\"message\":\"Account is not active\"}", await r.Content.ReadAsStringAsync());
        Assert.Equal(0, h.Tokens); Assert.Equal(0, h.Saves); Assert.Equal(0, h.Events);
    }

    [Fact]
    public async Task Inactive_account_with_a_wrong_password_stays_the_generic_401()
    {
        using var h = new SignInFixture();
        using var r = await h.SignIn("suspended", "wrong-password");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.DoesNotContain("not active", await r.Content.ReadAsStringAsync());
        Assert.Equal(0, h.Tokens);
    }

    [Theory]
    [InlineData("suspended")]
    [InlineData("pending")]
    public async Task Refresh_claims_issues_no_token_to_an_inactive_account(string who)
    {
        using var h = new SignInFixture();
        using var r = await h.Refresh(who);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal(0, h.Tokens);
    }
}

internal sealed class SignInFixture : IDisposable
{
    internal const string Password = "right-password";
    internal int Tokens { get; private set; }
    internal int Saves { get; private set; }
    internal int Events { get; private set; }
    private readonly Dictionary<string, User> users;
    private readonly TestServer server;
    private readonly HttpClient client;

    internal SignInFixture()
    {
        User Make(string name) => User.Create(Email.From("m328-" + name + "@example.invalid"), HashedPassword.FromHash("synthetic-hash"), AccessRole.User);
        users = new() { ["active"] = Make("active"), ["suspended"] = Make("suspended"), ["pending"] = Make("pending") };
        users["suspended"].Suspend("synthetic");
        // No production flow creates PendingVerification today; set it directly to cover the rule.
        typeof(User).GetProperty(nameof(User.Status))!.SetValue(users["pending"], EUserStatus.PendingVerification);
        foreach (var u in users.Values) u.ClearDomainEvents();
        var repository = AssignmentHttpFixture.Proxy<IUserRepository>((m, a) => m.Name switch
        {
            nameof(IUserRepository.FindByEmailAsync) => Task.FromResult(users.Values.SingleOrDefault(u => u.Email.Value == (string)a![0]!)),
            nameof(IUserRepository.FindByIdAsync) => Task.FromResult(users.Values.SingleOrDefault(u => u.Id == (UserId)a![0]!)),
            _ => throw new InvalidOperationException("Unexpected user call " + m.Name)
        });
        var hashing = AssignmentHttpFixture.Proxy<IHashingService>((m, a) => m.Name == nameof(IHashingService.VerifyPassword)
            ? (string)a![0]! == Password : throw new InvalidOperationException("Unexpected hashing call " + m.Name));
        var tokens = AssignmentHttpFixture.Proxy<ITokenService>((m, a) =>
        {
            if (m.Name != nameof(ITokenService.GenerateToken)) throw new InvalidOperationException("Unexpected token call " + m.Name);
            Tokens++; return "synthetic-token";
        });
        var unit = AssignmentHttpFixture.Proxy<IUnitOfWork>((m, a) => { Saves++; return Task.CompletedTask; });
        var mediator = AssignmentHttpFixture.Proxy<IMediator>((m, a) => { Events++; return Task.CompletedTask; });
        var profiles = AssignmentHttpFixture.Proxy<IProfilesContextFacade>((m, a) =>
            Task.FromResult<(string ProfileId, string ProfileStatus, string? BusinessRole, string? RoleSubjectId, string? SubscriptionTier)?>(null));
        var service = new UserCommandService(repository, tokens, hashing, unit, mediator, NullLogger<UserCommandService>.Instance, new IamProfiles(profiles));
        server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton<IUserCommandService>(service);
            services.AddControllers(o => o.Conventions.Add(new Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Configuration.KebabCaseRouteNamingConvention()))
                .AddApplicationPart(typeof(AuthenticationController).Assembly).ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlyAuthentication()));
        }).Configure(app =>
        {
            // refresh-claims needs the loaded user (IAM [Authorize]) and the JWT subject. The IAM middleware, which also blocks
            // inactive users since M3.27, is not in this pipeline, so the service-level rule is tested on its own.
            app.UseRouting(); app.Use(async (context, next) =>
            {
                if (context.Request.Headers.TryGetValue("X-Synthetic-User", out var who))
                {
                    // What the middleware would load for a valid token: the IAM user and its JWT subject.
                    context.Items["User"] = users[who!];
                    context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, users[who!].Id.Value)], "synthetic"));
                }
                await next();
            }); app.UseEndpoints(e => e.MapControllers());
        })); client = server.CreateClient();
    }

    internal Task<HttpResponseMessage> SignIn(string who, string password)
        => client.PostAsync("/api/v1/authentication/sign-in", JsonContent.Create(new { email = users[who].Email.Value, password }));

    internal Task<HttpResponseMessage> Refresh(string who)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/authentication/refresh-claims");
        request.Headers.Add("X-Synthetic-User", who);
        return client.SendAsync(request);
    }

    public void Dispose() { client.Dispose(); server.Dispose(); }

    private sealed class OnlyAuthentication : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        { foreach (var c in feature.Controllers.ToArray()) if (c.AsType() != typeof(AuthenticationController)) feature.Controllers.Remove(c); }
    }
}
