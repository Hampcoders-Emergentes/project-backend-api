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

namespace Electrolink.Backend.UnitTests.Profiles.Application.Internal.EventHandlers;

public class ProfilesEventHandlersTests
{
    private readonly Fake<IProfileRepository> _profiles = new();

    private Profile GivenByUser(Profile profile)
    {
        _profiles.Returns(nameof(IProfileRepository.FindByUserIdAsync), profile);
        return profile;
    }

    [Fact]
    public async Task ClientReviewSubmittedHandle_ForHomeowner_UpdatesHomeownerRating()
    {
        // Arrange
        var profile = Homeowner();
        _profiles.Returns(nameof(IProfileRepository.FindByHomeownerIdAsync), profile);
        var sut = new ClientReviewSubmittedEventHandler(_profiles.Object, new Fake<IUnitOfWork>().Object,
            NullLogger<ClientReviewSubmittedEventHandler>.Instance);

        // Act
        await sut.Handle(new ClientReviewSubmittedEvent(Hampcoders.Electrolink.API.Monitoring.Domain.Model.ValueObjects.ServiceExecutionId.NewId(),
            AssignmentId.NewAssignmentId(), Ids.Technician, profile.Homeowner!.HomeownerId.Value, 4, [], DateTime.UtcNow, DateTime.UtcNow),
            CancellationToken.None);

        // Assert
        Assert.Equal(4, profile.Homeowner.AverageRating);
    }

    [Fact]
    public async Task ClientReviewSubmittedHandle_ForUnknownReviewedId_DoesNotUpdate()
    {
        // Arrange
        var sut = new ClientReviewSubmittedEventHandler(_profiles.Object, new Fake<IUnitOfWork>().Object,
            NullLogger<ClientReviewSubmittedEventHandler>.Instance);

        // Act
        await sut.Handle(new ClientReviewSubmittedEvent(Hampcoders.Electrolink.API.Monitoring.Domain.Model.ValueObjects.ServiceExecutionId.NewId(),
            AssignmentId.NewAssignmentId(), Ids.Technician, "user-9", 4, [], DateTime.UtcNow, DateTime.UtcNow), CancellationToken.None);

        // Assert
        Assert.Empty(_profiles.Calls);
    }

    [Fact]
    public async Task EnterpriseSubscriptionFullyActiveHandle_WithProfile_UpdatesSubscriptionTier()
    {
        // Arrange
        var profile = GivenByUser(Company());
        var sut = new EnterpriseSubscriptionFullyActiveForProfileEventHandler(_profiles.Object, new Fake<IUnitOfWork>().Object,
            NullLogger<EnterpriseSubscriptionFullyActiveForProfileEventHandler>.Instance);

        // Act
        await sut.Handle(new EnterpriseSubscriptionFullyActiveEvent("sub-1", Ids.User, "ENTERPRISE_PLUS", 5, 10, DateTime.UtcNow),
            CancellationToken.None);

        // Assert
        Assert.Equal("ENTERPRISE_PLUS", profile.SubscriptionTier);
    }

    [Fact]
    public async Task PaymentFailedHandle_ForActiveProfile_SuspendsProfile()
    {
        // Arrange
        var profile = GivenByUser(Homeowner());
        var sut = new PaymentFailedEventHandler(_profiles.Object, new Fake<IUnitOfWork>().Object, new Fake<IMediator>().Object,
            NullLogger<PaymentFailedEventHandler>.Instance);

        // Act
        await sut.Handle(new PaymentFailedEvent("sub-1", Ids.User, profile.ProfileId.Value, "inv-1", DateTime.UtcNow), CancellationToken.None);

        // Assert
        Assert.Equal(EProfileStatus.Suspended, profile.Status);
    }

    [Fact]
    public async Task PaymentFailedHandle_ForIncompleteProfile_LeavesStatusUnchanged()
    {
        // Arrange
        var profile = GivenByUser(Incomplete());
        var sut = new PaymentFailedEventHandler(_profiles.Object, new Fake<IUnitOfWork>().Object, new Fake<IMediator>().Object,
            NullLogger<PaymentFailedEventHandler>.Instance);

        // Act
        await sut.Handle(new PaymentFailedEvent("sub-1", Ids.User, profile.ProfileId.Value, "inv-1", DateTime.UtcNow), CancellationToken.None);

        // Assert
        Assert.Equal(EProfileStatus.Incomplete, profile.Status);
    }

    [Fact]
    public async Task ServiceCompletedHandle_ForKnownTechnician_SavesPortfolioUpdate()
    {
        // Arrange
        var profile = Technician();
        _profiles.Returns(nameof(IProfileRepository.FindByTechnicianIdAsync), profile);
        var execution = Monitoring.MonitoringTestData.ReadyToComplete();
        execution.Complete(Monitoring.MonitoringTestData.Technician, DateTime.UtcNow);
        var sut = new ServiceCompletedEventHandler(_profiles.Object, new Fake<IUnitOfWork>().Object,
            NullLogger<ServiceCompletedEventHandler>.Instance);

        // Act
        await sut.Handle(execution.DomainEvents.OfType<ServiceCompletedEvent>().Single(), CancellationToken.None);

        // Assert
        Assert.Same(profile, _profiles.CallsTo(nameof(IProfileRepository.Update)).Single().Arg<Profile>());
    }

    [Fact]
    public async Task ServiceCompletedHandle_ForUnknownTechnician_DoesNotUpdate()
    {
        // Arrange
        var execution = Monitoring.MonitoringTestData.ReadyToComplete();
        execution.Complete(Monitoring.MonitoringTestData.Technician, DateTime.UtcNow);
        var sut = new ServiceCompletedEventHandler(_profiles.Object, new Fake<IUnitOfWork>().Object,
            NullLogger<ServiceCompletedEventHandler>.Instance);

        // Act
        await sut.Handle(execution.DomainEvents.OfType<ServiceCompletedEvent>().Single(), CancellationToken.None);

        // Assert
        Assert.Equal(0, _profiles.CallCount(nameof(IProfileRepository.Update)));
    }

    [Fact]
    public async Task SubscriptionActivatedHandle_WithProfile_UpdatesSubscriptionTier()
    {
        // Arrange
        var profile = GivenByUser(Technician());
        var sut = new SubscriptionActivatedForProfileEventHandler(_profiles.Object, new Fake<IUnitOfWork>().Object,
            NullLogger<SubscriptionActivatedForProfileEventHandler>.Instance);

        // Act
        await sut.Handle(new SubscriptionActivatedEvent("sub-1", Ids.User, "TECHNICIAN", "PRO", "MONTHLY", "st-1", DateTime.UtcNow,
            DateTime.UtcNow.AddMonths(1), DateTime.UtcNow), CancellationToken.None);

        // Assert
        Assert.Equal("PRO", profile.SubscriptionTier);
    }

    [Fact]
    public async Task SubscriptionActivatedHandle_WithoutProfile_DoesNotUpdate()
    {
        // Arrange
        var sut = new SubscriptionActivatedForProfileEventHandler(_profiles.Object, new Fake<IUnitOfWork>().Object,
            NullLogger<SubscriptionActivatedForProfileEventHandler>.Instance);

        // Act
        await sut.Handle(new SubscriptionActivatedEvent("sub-1", Ids.User, "TECHNICIAN", "PRO", "MONTHLY", "st-1", DateTime.UtcNow,
            DateTime.UtcNow.AddMonths(1), DateTime.UtcNow), CancellationToken.None);

        // Assert
        Assert.Equal(0, _profiles.CallCount(nameof(IProfileRepository.Update)));
    }

    [Fact]
    public async Task SubscriptionInitializedHandle_WithProfile_UpdatesSubscriptionTier()
    {
        // Arrange
        var profile = GivenByUser(Homeowner());
        var sut = new SubscriptionInitializedForProfileEventHandler(_profiles.Object, new Fake<IUnitOfWork>().Object,
            NullLogger<SubscriptionInitializedForProfileEventHandler>.Instance);

        // Act
        await sut.Handle(new SubscriptionInitializedEvent("sub-1", Ids.User, profile.ProfileId.Value, "HOMEOWNER", "FREE", "cus-1", DateTime.UtcNow),
            CancellationToken.None);

        // Assert
        Assert.Equal("FREE", profile.SubscriptionTier);
    }

    [Fact]
    public async Task TechnicianEvaluationSubmittedHandle_ForKnownTechnician_UpdatesRating()
    {
        // Arrange
        var profile = Technician();
        _profiles.Returns(nameof(IProfileRepository.FindByTechnicianIdAsync), profile);
        var sut = new TechnicianEvaluationSubmittedEventHandler(_profiles.Object, new Fake<IUnitOfWork>().Object,
            NullLogger<TechnicianEvaluationSubmittedEventHandler>.Instance);

        // Act
        await sut.Handle(new TechnicianReviewSubmittedEvent(Hampcoders.Electrolink.API.Monitoring.Domain.Model.ValueObjects.ServiceExecutionId.NewId(),
            AssignmentId.NewAssignmentId(), "ho-1", profile.Technician!.TechnicianId.Value, 5, [], DateTime.UtcNow, DateTime.UtcNow),
            CancellationToken.None);

        // Assert
        Assert.Equal(5, profile.Technician.AverageRating);
    }

    [Fact]
    public async Task UserRegisteredHandle_WithEvent_CreatesProfileForUser()
    {
        // Arrange
        var commands = new Fake<IProfileCommandService>();
        var sut = new UserRegisteredEventHandler(commands.Object, NullLogger<UserRegisteredEventHandler>.Instance);

        // Act
        await sut.Handle(new UserRegisteredEvent(Ids.User, "ana@electrolink.pe", DateTime.UtcNow, "User"), CancellationToken.None);

        // Assert
        Assert.Equal(new CreateProfileCommand(Ids.User), commands.Calls.Single().Arg<CreateProfileCommand>());
    }
}
