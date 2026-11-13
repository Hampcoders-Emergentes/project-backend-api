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

public class SubscriptionsOutboundServicesTests
{
    [Fact]
    public void EnterpriseBillingPolicy_Initialize_StartsWithZeroDevicesAndNoCounters()
    {
        // Arrange
        var sut = new EnterpriseBillingPolicy();

        // Act
        var result = (sut.InitializeCounters(), sut.InitializeActiveDeviceCount());

        // Assert
        Assert.Equal(((UsageCounters?)null, (int?)0), result);
    }

    [Fact]
    public void HomeownerBillingPolicy_Initialize_StartsWithTwoFreeRequests()
    {
        // Arrange
        var sut = new HomeownerBillingPolicy();

        // Act
        var counters = sut.InitializeCounters();

        // Assert
        Assert.Equal((0, 2, (int?)null), (counters!.MonthlyRequestsUsed, counters.MonthlyRequestsLimit, sut.InitializeActiveDeviceCount()));
    }

    [Fact]
    public void HomeownerBillingPolicy_ProcessIncrementOnBasic_ConsumesRequest()
    {
        // Arrange
        var subscription = Basic();
        var sut = new HomeownerBillingPolicy();

        // Act
        sut.ProcessIncrement(subscription);

        // Assert
        Assert.Equal(1, subscription.UsageCounters!.MonthlyRequestsUsed);
    }

    [Fact]
    public void HomeownerBillingPolicy_ProcessReset_ClearsUsedRequests()
    {
        // Arrange
        var subscription = Basic();
        subscription.IncrementRequestCounter();
        var sut = new HomeownerBillingPolicy();

        // Act
        sut.ProcessReset(subscription);

        // Assert
        Assert.Equal(0, subscription.UsageCounters!.MonthlyRequestsUsed);
    }

    [Fact]
    public async Task ExternalIamService_GetUserEmailAsync_ReturnsIamEmail()
    {
        // Arrange
        var iam = new Fake<IIamContextFacade>().Returns(nameof(IIamContextFacade.FetchEmailByUserId), "ana@electrolink.pe");
        var sut = new ExternalIamService(iam.Object);

        // Act
        var result = await sut.GetUserEmailAsync(Ids.User);

        // Assert
        Assert.Equal("ana@electrolink.pe", result);
    }

    [Fact]
    public async Task ExternalIamService_UserExistsAsync_ReturnsIamResult()
    {
        // Arrange
        var iam = new Fake<IIamContextFacade>().Returns(nameof(IIamContextFacade.UserExistsAsync), true);
        var sut = new ExternalIamService(iam.Object);

        // Act
        var result = await sut.UserExistsAsync(Ids.User);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ExternalProfileService_FetchProfileFullName_ResolvesThroughProfileClaims()
    {
        // Arrange
        var profiles = new Fake<IProfilesContextFacade>()
            .Returns(nameof(IProfilesContextFacade.GetProfileClaimsAsync), ("prof-1", "Active", (string?)"HomeOwner", (string?)"ho-1", (string?)null))
            .On(nameof(IProfilesContextFacade.GetProfileFullNameAsync), a => (string)a[0]! == "prof-1" ? "Ana Diaz" : null);
        var sut = new ExternalProfileService(profiles.Object, NullLogger<ExternalProfileService>.Instance);

        // Act
        var result = await sut.FetchProfileFullName(Ids.User);

        // Assert
        Assert.Equal("Ana Diaz", result);
    }

    [Fact]
    public async Task ExternalProfileService_FetchProfileFullNameWithoutProfile_ReturnsNull()
    {
        // Arrange
        var sut = new ExternalProfileService(new Fake<IProfilesContextFacade>().Object, NullLogger<ExternalProfileService>.Instance);

        // Act
        var result = await sut.FetchProfileFullName(Ids.User);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ExternalProfileService_IsTechnicianAsync_ReturnsProfilesResult()
    {
        // Arrange
        var profiles = new Fake<IProfilesContextFacade>().Returns(nameof(IProfilesContextFacade.ExistsTechnicianProfileByUserIdAsync), true);
        var sut = new ExternalProfileService(profiles.Object, NullLogger<ExternalProfileService>.Instance);

        // Act
        var result = await sut.IsTechnicianAsync(Ids.User);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ExternalProfileService_GetProfilePhoneAsync_ReturnsProfilesPhone()
    {
        // Arrange
        var profiles = new Fake<IProfilesContextFacade>().Returns(nameof(IProfilesContextFacade.GetProfilePhoneAsync), "+51999");
        var sut = new ExternalProfileService(profiles.Object, NullLogger<ExternalProfileService>.Instance);

        // Act
        var result = await sut.GetProfilePhoneAsync("prof-1");

        // Assert
        Assert.Equal("+51999", result);
    }
}
