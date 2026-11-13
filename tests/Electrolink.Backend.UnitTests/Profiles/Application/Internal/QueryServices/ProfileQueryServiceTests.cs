using System.Net;
using System.Text;
using System.Text.Json;
using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Assets.Interfaces.ACL;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Events;
using Hampcoders.Electrolink.API.IAM.Interfaces.ACL;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Events;
using Hampcoders.Electrolink.API.Profiles.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Profiles.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Profiles.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Profiles.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Profiles.Domain.Repositories;
using Hampcoders.Electrolink.API.Profiles.Domain.Services;
using Hampcoders.Electrolink.API.Profiles.Infrastructure.ExternalProviders;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Events;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Electrolink.Backend.UnitTests.Profiles.ProfilesTestData;
using ServiceCompletedEventHandler = Hampcoders.Electrolink.API.Profiles.Application.Internal.EventHandlers.ServiceCompletedEventHandler;

namespace Electrolink.Backend.UnitTests.Profiles.Application.Internal.QueryServices;

public class ProfileQueryServiceTests
{
    private readonly Fake<IProfileRepository> _profiles = new();

    [Fact]
    public async Task Handle_GetById_ReturnsRepositoryProfile()
    {
        // Arrange
        var profile = Homeowner();
        _profiles.Returns(nameof(IProfileRepository.FindByIdAsync), profile);
        var sut = new ProfileQueryService(_profiles.Object);

        // Act
        var result = await sut.Handle(new GetProfileByIdQuery(profile.ProfileId.Value));

        // Assert
        Assert.Same(profile, result);
    }

    [Fact]
    public async Task Handle_GetMyProfile_LooksUpByUser()
    {
        // Arrange
        var profile = Homeowner();
        _profiles.Returns(nameof(IProfileRepository.FindByUserIdAsync), profile);
        var sut = new ProfileQueryService(_profiles.Object);

        // Act
        var result = await sut.Handle(new GetMyProfileQuery(Ids.User));

        // Assert
        Assert.Same(profile, result);
    }

    [Fact]
    public async Task Handle_GetStatusWithoutProfile_ReturnsNull()
    {
        // Arrange
        var sut = new ProfileQueryService(_profiles.Object);

        // Act
        var result = await sut.Handle(new GetProfileStatusQuery(Ids.User));

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData(false, 50)]
    [InlineData(true, 100)]
    public async Task Handle_GetStatus_ReportsCompletionPercentageByStatus(bool completed, int expected)
    {
        // Arrange
        _profiles.Returns(nameof(IProfileRepository.FindByUserIdAsync), completed ? Homeowner() : Incomplete());
        var sut = new ProfileQueryService(_profiles.Object);

        // Act
        var result = await sut.Handle(new GetProfileStatusQuery(Ids.User));

        // Assert
        Assert.Equal(expected, result!.CompletionPercentage);
    }


    [Fact]
    public async Task Handle_GetClaims_ReturnsRepositoryClaims()
    {
        // Arrange
        var claims = ("prof-1", "Active", (string?)"HomeOwner", (string?)"ho-1", (string?)null);
        _profiles.Returns(nameof(IProfileRepository.FindProfileClaimsByUserIdAsync), claims);
        var sut = new ProfileQueryService(_profiles.Object);

        // Act
        var result = await sut.Handle(new GetProfileClaimsQuery(Ids.User));

        // Assert
        Assert.Equal(claims, result);
    }

    [Fact]
    public async Task Handle_IsHomeownerActive_ReturnsRepositoryFlag()
    {
        // Arrange
        _profiles.Returns(nameof(IProfileRepository.IsHomeownerActiveAsync), true);
        var sut = new ProfileQueryService(_profiles.Object);

        // Act
        var result = await sut.Handle(new IsHomeownerActiveQuery(HomeownerId.From("ho-1")));

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task Handle_GetTechniciansInArea_ForwardsCoordinates()
    {
        // Arrange
        var sut = new ProfileQueryService(_profiles.Object);

        // Act
        await sut.Handle(new GetTechniciansInAreaQuery(-12.1, -77.0));

        // Assert
        Assert.Equal(new object?[] { -12.1, -77.0 }, _profiles.Calls.Single().Args);
    }
}
