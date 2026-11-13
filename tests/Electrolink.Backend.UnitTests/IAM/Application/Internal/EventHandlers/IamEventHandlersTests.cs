using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.IAM.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.IAM.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Events;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Queries;
using Hampcoders.Electrolink.API.IAM.Domain.Repositories;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.StoreManagement.Interfaces.ACL;
using Microsoft.Extensions.Logging.Abstractions;
using static Electrolink.Backend.UnitTests.IAM.IamTestData;

namespace Electrolink.Backend.UnitTests.IAM.Application.Internal.EventHandlers;

public class PasswordResetRequestedEventHandlerTests
{
    private static PasswordResetRequestedEvent Event() =>
        new(Ids.User, UserEmail, "secret-token", DateTime.UtcNow.AddHours(1), DateTime.UtcNow);

    [Fact]
    public async Task Handle_WithEvent_SendsTokenToEmail()
    {
        // Arrange
        var notifier = new Fake<IPasswordResetNotificationPort>();
        var sut = new PasswordResetRequestedEventHandler(notifier.Object, NullLogger<PasswordResetRequestedEventHandler>.Instance);

        // Act
        await sut.Handle(Event(), CancellationToken.None);

        // Assert
        var call = notifier.Calls.Single();
        Assert.Equal((UserEmail, "secret-token"), (call.Arg<string>(0), call.Arg<string>(1)));
    }

    [Fact]
    public async Task Handle_WhenDeliveryFails_SwallowsException()
    {
        // Arrange
        var notifier = new Fake<IPasswordResetNotificationPort>().Throws(nameof(IPasswordResetNotificationPort.SendAsync),
            new InvalidOperationException("smtp down"));
        var sut = new PasswordResetRequestedEventHandler(notifier.Object, NullLogger<PasswordResetRequestedEventHandler>.Instance);

        // Act
        var exception = await Record.ExceptionAsync(() => sut.Handle(Event(), CancellationToken.None));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task Handle_WhenDeliveryIsCancelled_PropagatesCancellation()
    {
        // Arrange
        var notifier = new Fake<IPasswordResetNotificationPort>().Throws(nameof(IPasswordResetNotificationPort.SendAsync),
            new OperationCanceledException());
        var sut = new PasswordResetRequestedEventHandler(notifier.Object, NullLogger<PasswordResetRequestedEventHandler>.Instance);

        // Act
        var act = () => sut.Handle(Event(), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(act);
    }
}

public class IamLoggingEventHandlersTests
{
    [Fact]
    public async Task UserPasswordChangedHandle_WithEvent_CompletesSuccessfully()
    {
        // Arrange
        var user = NewUser();
        user.UpdatePasswordHash(Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects.HashedPassword.FromHash("h2"));
        var sut = new UserPasswordChangedEventHandler(NullLogger<UserPasswordChangedEventHandler>.Instance);

        // Act
        var task = sut.Handle(user.DomainEvents.OfType<UserPasswordChangedEvent>().Single(), CancellationToken.None);

        // Assert
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task UserSignedInHandle_WithEvent_CompletesSuccessfully()
    {
        // Arrange
        var sut = new UserSignedInEventHandler(NullLogger<UserSignedInEventHandler>.Instance);

        // Act
        var task = sut.Handle(new UserSignedInEvent(Ids.User, DateTime.UtcNow), CancellationToken.None);

        // Assert
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }
}
