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

namespace Electrolink.Backend.UnitTests.Subscriptions.Application.Internal.CommandServices;

public class ContactRequestCommandServiceTests
{
    private readonly Fake<IContactRequestRepository> _repository = new();
    private readonly Fake<IEmailSender> _email = new();

    private ContactRequestCommandService CreateSut(string? inbox = "sales@electrolink.pe") => new(_repository.Object, _email.Object,
        Options.Create(new DemoRequestSettings { SalesInbox = inbox, MaxRequestsPerEmailPerDay = 3 }), NullLogger<ContactRequestCommandService>.Instance);

    private static SubmitContactRequestCommand Command(string email = "lead@acme.pe") =>
        new("Luis", "Acme", email, "+51 999 111 222", "We need a demo", "10.0.0.1");

    [Fact]
    public async Task Handle_WhenDailyLimitReached_ThrowsContactRequestLimitException()
    {
        // Arrange
        _repository.Returns(nameof(IContactRequestRepository.CountFromEmailSince), 3);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Command(), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ContactRequestLimitException>(act);
    }

    [Fact]
    public async Task Handle_WithInvalidEmail_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Command("not-an-email"), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_WithSalesInbox_NotifiesSalesAndConfirmsToProspect()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var receipt = await sut.Handle(Command(), CancellationToken.None);

        // Assert
        Assert.Equal(ContactRequest.Notified, receipt.Status);
        Assert.Equal(["sales@electrolink.pe", "lead@acme.pe"], _email.Calls.Select(c => c.Arg<EmailMessage>().To));
    }

    [Fact]
    public async Task Handle_WithoutSalesInbox_StoresRequestAndOnlyConfirmsToProspect()
    {
        // Arrange
        var sut = CreateSut(inbox: null);

        // Act
        var receipt = await sut.Handle(Command(), CancellationToken.None);

        // Assert
        Assert.Equal(ContactRequest.Received, receipt.Status);
        Assert.Equal("lead@acme.pe", _email.Calls.Single().Arg<EmailMessage>().To);
    }

    [Fact]
    public async Task Handle_WhenEmailDeliveryFails_StillReturnsReceipt()
    {
        // Arrange
        _email.Throws(nameof(IEmailSender.SendAsync), new InvalidOperationException("smtp down"));
        var sut = CreateSut();

        // Act
        var receipt = await sut.Handle(Command(), CancellationToken.None);

        // Assert
        Assert.Equal(ContactRequest.Received, receipt.Status);
        Assert.Equal(1, _repository.CallCount(nameof(IContactRequestRepository.Add)));
    }
}
