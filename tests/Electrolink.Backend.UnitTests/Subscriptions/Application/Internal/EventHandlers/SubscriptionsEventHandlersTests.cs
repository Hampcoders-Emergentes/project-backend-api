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

namespace Electrolink.Backend.UnitTests.Subscriptions.Application.Internal.EventHandlers;

public class SubscriptionsEventHandlersTests
{
    private readonly Fake<ISubscriptionRepository> _subscriptions = new();
    private readonly Fake<ISubscriptionCommandService> _commands = new();

    [Fact]
    public async Task DeviceInstalledHandle_ForPendingInstallation_ActivatesSubscriptionFully()
    {
        // Arrange
        var subscription = EnterprisePendingInstallation();
        _subscriptions.Returns(nameof(ISubscriptionRepository.FindByPendingInstallationServiceRequestIdAsync), subscription);
        var sut = new DeviceInstalledEventHandler(_subscriptions.Object, new Fake<IUnitOfWork>().Object, new Fake<IMediator>().Object,
            NullLogger<DeviceInstalledEventHandler>.Instance);

        // Act
        await sut.Handle(new DeviceInstalledEvent("dev-1", "SN-1", "prop-1", Ids.Technician, "req-1", "1.0", DateTime.UtcNow),
            CancellationToken.None);

        // Assert
        Assert.Equal((SubscriptionStatus.Active, 1), (subscription.Status, subscription.ActiveDeviceCount!.Value));
    }

    [Fact]
    public async Task DeviceInstalledHandle_WithoutPendingSubscription_DoesNotUpdate()
    {
        // Arrange
        var sut = new DeviceInstalledEventHandler(_subscriptions.Object, new Fake<IUnitOfWork>().Object, new Fake<IMediator>().Object,
            NullLogger<DeviceInstalledEventHandler>.Instance);

        // Act
        await sut.Handle(new DeviceInstalledEvent("dev-1", "SN-1", "prop-1", Ids.Technician, "req-1", "1.0", DateTime.UtcNow),
            CancellationToken.None);

        // Assert
        Assert.Equal(0, _subscriptions.CallCount(nameof(ISubscriptionRepository.Update)));
    }

    [Fact]
    public async Task ProfileCompletedHandle_WithEvent_CreatesSubscriptionForProfile()
    {
        // Arrange
        var sut = new ProfileCompletedEventHandler(_commands.Object);

        // Act
        await sut.Handle(new ProfileCompletedEvent("prof-1", Ids.User, "ho-1", EBusinessRole.HomeOwner, DateTime.UtcNow), CancellationToken.None);

        // Assert
        Assert.Equal(new CreateSubscriptionCommand(UserId.From(Ids.User), ProfileId.From("prof-1")), _commands.Calls.Single().Args[0]);
    }

    [Theory]
    [InlineData("ho-1", nameof(IProfilesContextFacade.GetProfileIdByHomeownerIdAsync))]
    [InlineData("comp-1", nameof(IProfilesContextFacade.GetProfileIdByCompanyIdAsync))]
    public async Task ServiceRequestCreatedHandle_ForClient_IncrementsCounterOfResolvedProfile(string client, string lookup)
    {
        // Arrange
        var profiles = new Fake<IProfilesContextFacade>().Returns(lookup, "prof-7");
        var sut = new ServiceRequestCreatedEventHandler(_commands.Object, profiles.Object);

        // Act
        await sut.Handle(RequestCreated(ClientIdentity.FromOwnerId(client)), CancellationToken.None);

        // Assert
        Assert.Equal(new IncrementMonthlyRequestCounterCommand("prof-7"), _commands.Calls.Single().Args[0]);
    }

    [Fact]
    public async Task ServiceRequestCreatedHandle_WhenProfileUnresolved_DoesNotIncrement()
    {
        // Arrange
        var sut = new ServiceRequestCreatedEventHandler(_commands.Object, new Fake<IProfilesContextFacade>().Object);

        // Act
        await sut.Handle(RequestCreated(ClientIdentity.FromHomeowner("ho-1")), CancellationToken.None);

        // Assert
        Assert.Empty(_commands.Calls);
    }

    private static ServiceRequestCreatedEvent RequestCreated(ClientIdentity client) => new(RequestId.NewId(), client, PropertyId.NewPropertyId(),
        RecipeId.NewId(), TechnicianId.From(Ids.Technician), Planning.PlanningTestData.Recipe() is var r
            ? Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects.RecipeSnapshot.FromRecipe(r) : null!, false, DateTime.UtcNow);
}
