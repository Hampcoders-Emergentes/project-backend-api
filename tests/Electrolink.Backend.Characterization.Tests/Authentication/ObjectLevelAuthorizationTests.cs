using System.Security.Claims;
using Electrolink.Backend.Characterization.Tests.Assets;
using Hampcoders.Electrolink.API.Analytics.Domain.Services;
using Hampcoders.Electrolink.API.Analytics.Interfaces.REST;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Monitoring.Domain.Services;
using Hampcoders.Electrolink.API.Monitoring.Interfaces.REST;
using Hampcoders.Electrolink.API.Profiles.Domain.Services;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Profiles.Interfaces.REST;
using Hampcoders.Electrolink.API.Profiles.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Shared.Application.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Electrolink.Backend.Characterization.Tests.Authentication;

public sealed class ObjectLevelAuthorizationTests
{
    private const string OwnHomeowner = "ho-own";
    private const string OwnProfile = "prof-own";

    private static readonly User Actor = User.Create(Email.From("idor@example.invalid"), HashedPassword.FromHash("synthetic-unused-hash"), AccessRole.User);

    [Fact]
    public async Task Subject_is_resolved_from_the_user_id_through_profiles()
    {
        var subject = await ActorSubjectResolver.ResolveAsync(Profiles("HomeOwner", OwnHomeowner), Actor.Id.Value);

        Assert.NotNull(subject);
        Assert.Equal(OwnProfile, subject.ProfileId);
        Assert.True(subject.IsClient(OwnHomeowner));
        Assert.False(subject.IsClient("ho-other"));
        Assert.Null(subject.Technician);
    }

    [Theory]
    [InlineData("Suspended", OwnHomeowner)]
    [InlineData("Active", null)]
    public async Task Ineligible_profile_resolves_to_no_subject(string status, string? roleSubject)
        => Assert.Null(await ActorSubjectResolver.ResolveAsync(Profiles("HomeOwner", roleSubject, status), Actor.Id.Value));

    [Fact]
    public async Task Thresholds_of_another_profile_are_not_written()
    {
        var writes = 0;
        var profiles = Proxy<IProfilesContextFacade>((method, args) =>
        {
            if (method.Name == nameof(IProfilesContextFacade.SetConsumptionThresholdsAsync)) { writes++; return Task.CompletedTask; }
            return ProfileClaims("HomeOwner", OwnHomeowner);
        });
        var controller = WithActor(new ProfilesController(Unexpected<IProfileCommandService>(), Unexpected<IProfileQueryService>(), profiles));
        var resource = new SetThresholdsResource(new Dictionary<string, decimal>());

        Assert.IsType<NotFoundObjectResult>(await controller.SetThresholds("prof-other", resource));
        Assert.Equal(0, writes);

        Assert.IsType<NoContentResult>(await controller.SetThresholds(OwnProfile, resource));
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task Service_history_of_another_client_is_not_queried()
    {
        var controller = WithActor(new ServiceExecutionsController(
            Unexpected<IServiceExecutionCommandService>(), Unexpected<IServiceExecutionQueryService>(), Profiles("HomeOwner", OwnHomeowner)));

        var response = await controller.GetHistoryByHomeowner("ho-other");

        Assert.IsType<NotFoundObjectResult>(response.Result);
    }

    [Fact]
    public async Task Technician_routes_reject_a_client_actor()
    {
        var controller = WithActor(new ServiceExecutionsController(
            Unexpected<IServiceExecutionCommandService>(), Unexpected<IServiceExecutionQueryService>(), Profiles("HomeOwner", OwnHomeowner)));

        var response = await controller.GetAssignedByTechnician("me");

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<StatusCodeResult>(response.Result).StatusCode);
    }

    [Fact]
    public async Task Dashboard_of_another_client_is_not_queried()
    {
        var controller = WithActor(new ConsumptionDashboardController(
            Unexpected<IConsumptionDashboardQueryService>(), Unexpected<IConsumptionDashboardCommandService>(),
            Profiles("HomeOwner", OwnHomeowner), NullLogger<ConsumptionDashboardController>.Instance));

        Assert.IsType<NotFoundObjectResult>(await controller.GetDashboardView("ho-other"));
    }

    private static T WithActor<T>(T controller) where T : ControllerBase
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Actor.Id.Value) })) };
        context.Items["User"] = Actor;
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private static T Unexpected<T>() where T : class =>
        Proxy<T>((method, args) => throw new InvalidOperationException("Unexpected call: " + method.Name));

    private static T Proxy<T>(Func<System.Reflection.MethodInfo, object?[]?, object?> call) where T : class =>
        AssignmentHttpFixture.Proxy<T>(call);

    private static IProfilesContextFacade Profiles(string role, string? roleSubject, string status = "Active") =>
        Proxy<IProfilesContextFacade>((method, args) => method.Name switch
        {
            nameof(IProfilesContextFacade.GetProfileClaimsAsync) => ProfileClaims(role, roleSubject, status),
            nameof(IProfilesContextFacade.GetProfileIdByHomeownerIdAsync) => Task.FromResult<string?>(OwnProfile),
            _ => throw new InvalidOperationException("Unexpected profile call: " + method.Name)
        });

    private static Task<(string ProfileId, string ProfileStatus, string? BusinessRole, string? RoleSubjectId, string? SubscriptionTier)?> ProfileClaims(
        string role, string? roleSubject, string status = "Active") =>
        Task.FromResult<(string ProfileId, string ProfileStatus, string? BusinessRole, string? RoleSubjectId, string? SubscriptionTier)?>(
            (OwnProfile, status, role, roleSubject, null));
}
