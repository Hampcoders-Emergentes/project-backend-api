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

namespace Electrolink.Backend.UnitTests.Subscriptions.Application.Internal.QueryServices;

public class SubscriptionQueryServiceTests
{
    private readonly Fake<ISubscriptionRepository> _subscriptions = new();

    private Subscription GivenByProfile(Subscription subscription)
    {
        _subscriptions.Returns(nameof(ISubscriptionRepository.FindByProfileIdOrFailAsync), subscription);
        return subscription;
    }

    [Fact]
    public async Task Handle_GetMySubscription_ReturnsProfileSubscription()
    {
        // Arrange
        var subscription = GivenByProfile(Basic());
        var sut = new SubscriptionQueryService(_subscriptions.Object);

        // Act
        var result = await sut.Handle(new GetMySubscriptionQuery(Profile.Value));

        // Assert
        Assert.Same(subscription, result);
    }

    [Fact]
    public async Task Handle_GetEligibilityForPremium_AllowsUnlimitedPriorityRequests()
    {
        // Arrange
        GivenByProfile(Premium());
        var sut = new SubscriptionQueryService(_subscriptions.Object);

        // Act
        var result = await sut.Handle(new GetRequestEligibilityQuery(Profile.Value));

        // Assert
        Assert.Equal(new RequestEligibilityResult(true, true, null, "PREMIUM", false), result);
    }

    [Fact]
    public async Task Handle_GetEligibilityForBasicWithCapacity_AllowsRequestWithoutPriority()
    {
        // Arrange
        GivenByProfile(Basic());
        var sut = new SubscriptionQueryService(_subscriptions.Object);

        // Act
        var result = await sut.Handle(new GetRequestEligibilityQuery(Profile.Value));

        // Assert
        Assert.Equal((true, false, 2, false), (result.CanRequest, result.IsPriorityAllowed, result.RemainingRequests, result.UpgradeRequired));
    }

    [Fact]
    public async Task Handle_GetEligibilityForBasicAtLimit_RequiresUpgrade()
    {
        // Arrange
        var subscription = GivenByProfile(Basic());
        subscription.IncrementRequestCounter();
        subscription.IncrementRequestCounter();
        var sut = new SubscriptionQueryService(_subscriptions.Object);

        // Act
        var result = await sut.Handle(new GetRequestEligibilityQuery(Profile.Value));

        // Assert
        Assert.Equal((false, 0, true), (result.CanRequest, result.RemainingRequests, result.UpgradeRequired));
    }

    [Fact]
    public async Task Handle_GetStatusAlertOutsideGracePeriod_ReturnsNull()
    {
        // Arrange
        GivenByProfile(Premium());
        var sut = new SubscriptionQueryService(_subscriptions.Object);

        // Act
        var result = await sut.Handle(new GetSubscriptionStatusAlertQuery(Profile.Value));

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_GetStatusAlertInGracePeriod_ReturnsSubscription()
    {
        // Arrange
        var subscription = GivenByProfile(InGracePeriod());
        var sut = new SubscriptionQueryService(_subscriptions.Object);

        // Act
        var result = await sut.Handle(new GetSubscriptionStatusAlertQuery(Profile.Value));

        // Assert
        Assert.Same(subscription, result);
    }

    [Fact]
    public async Task Handle_GetPaymentHistory_QueriesBySubscriptionAndPage()
    {
        // Arrange
        var subscription = GivenByProfile(Basic());
        var sut = new SubscriptionQueryService(_subscriptions.Object);

        // Act
        await sut.Handle(new GetPaymentHistoryQuery(Profile.Value, 2, 5));

        // Assert
        Assert.Equal(new object?[] { subscription.SubscriptionId.Value, 2, 5 },
            _subscriptions.CallsTo(nameof(ISubscriptionRepository.FindPaymentHistoryAsync)).Single().Args);
    }

    [Fact]
    public async Task GetTierAsync_WithSubscription_ReturnsPlanName()
    {
        // Arrange
        _subscriptions.Returns(nameof(ISubscriptionRepository.FindByUserIdAsync), Premium());
        var sut = new SubscriptionTierQueryService(_subscriptions.Object);

        // Act
        var result = await sut.GetTierAsync(Ids.User);

        // Assert
        Assert.Equal("PREMIUM", result);
    }

    [Fact]
    public async Task GetTierAsync_WithoutSubscription_ReturnsNull()
    {
        // Arrange
        var sut = new SubscriptionTierQueryService(_subscriptions.Object);

        // Act
        var result = await sut.GetTierAsync(Ids.User);

        // Assert
        Assert.Null(result);
    }
}
