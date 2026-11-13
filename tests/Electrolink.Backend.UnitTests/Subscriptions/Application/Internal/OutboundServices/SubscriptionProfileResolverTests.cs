using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Events;
using Hampcoders.Electrolink.API.IAM.Interfaces.ACL;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Events;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Events;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Application.Notifications;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Repository;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Services;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.REST.Transform;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Electrolink.Backend.UnitTests.Subscriptions.SubscriptionsTestData;
using EBusinessRole = Hampcoders.Electrolink.API.Profiles.Domain.Model.ValueObjects.EBusinessRole;
using Money = Hampcoders.Electrolink.API.Subscriptions.Domain.Model.ValueObjects.Money;
using IContactRequestRepository = Hampcoders.Electrolink.API.Subscriptions.Domain.Repository.IContactRequestRepository;

namespace Electrolink.Backend.UnitTests.Subscriptions.Application.Internal.OutboundServices;

public class SubscriptionProfileResolverTests
{
    private static Fake<IProfilesContextFacade> Profiles(string? role, string status = "Active") => new Fake<IProfilesContextFacade>()
        .Returns(nameof(IProfilesContextFacade.GetProfileClaimsAsync), ("prof-1", status, role, (string?)"ho-1", (string?)null));

    [Fact]
    public async Task ResolveBusinessRoleAsync_WithHomeownerClaims_ReturnsHomeownerRole()
    {
        // Arrange
        var sut = new SubscriptionProfileResolver(Profiles("HomeOwner").Object);

        // Act
        var result = await sut.ResolveBusinessRoleAsync(Ids.User);

        // Assert
        Assert.Equal(BusinessRole.Homeowner, result);
    }

    [Fact]
    public async Task ResolveBusinessRoleAsync_WithoutRole_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = new SubscriptionProfileResolver(Profiles(null).Object);

        // Act
        var act = () => sut.ResolveBusinessRoleAsync(Ids.User);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task ResolveProfileFullNameAsync_WithClaims_ReturnsProfileName()
    {
        // Arrange
        var profiles = Profiles("HomeOwner").Returns(nameof(IProfilesContextFacade.GetProfileFullNameAsync), "Ana Diaz");
        var sut = new SubscriptionProfileResolver(profiles.Object);

        // Act
        var result = await sut.ResolveProfileFullNameAsync(Ids.User);

        // Assert
        Assert.Equal("Ana Diaz", result);
    }

    [Theory]
    [InlineData("Active", "prof-1")]
    [InlineData("Suspended", "prof-1")]
    [InlineData("Incomplete", null)]
    public async Task ResolveSubscriptionProfileIdAsync_ByProfileStatus_ReturnsIdOnlyForBillableProfiles(string status, string? expected)
    {
        // Arrange
        var sut = new SubscriptionProfileResolver(Profiles("HomeOwner", status).Object);

        // Act
        var result = await sut.ResolveSubscriptionProfileIdAsync(Ids.User);

        // Assert
        Assert.Equal(expected, result);
    }
}
