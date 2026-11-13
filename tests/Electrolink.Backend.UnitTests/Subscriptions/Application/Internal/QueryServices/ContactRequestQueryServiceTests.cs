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

public class ContactRequestQueryServiceTests
{
    private readonly Fake<IContactRequestRepository> _repository = new();

    [Theory]
    [InlineData("")]
    [InlineData("01234567890123456789012345678901234567890")]
    public async Task Handle_GetByIdWithInvalidId_ReturnsNullWithoutQuerying(string id)
    {
        // Arrange
        var sut = new ContactRequestQueryService(_repository.Object);

        // Act
        var result = await sut.Handle(new GetContactRequestByIdQuery(id), CancellationToken.None);

        // Assert
        Assert.Null(result);
        Assert.Empty(_repository.Calls);
    }

    [Fact]
    public async Task Handle_GetById_ReturnsRepositoryRequest()
    {
        // Arrange
        var request = ContactRequest.Submit("Luis", "Acme", "lead@acme.pe", null, "demo", null, DateTime.UtcNow);
        _repository.Returns(nameof(IContactRequestRepository.FindById), request);
        var sut = new ContactRequestQueryService(_repository.Object);

        // Act
        var result = await sut.Handle(new GetContactRequestByIdQuery(request.Id), CancellationToken.None);

        // Assert
        Assert.Same(request, result);
    }

    [Fact]
    public async Task Handle_ListWithUnknownStatus_ThrowsArgumentException()
    {
        // Arrange
        var sut = new ContactRequestQueryService(_repository.Object);

        // Act
        var act = () => sut.Handle(new ListContactRequestsQuery("ARCHIVED", 0, 10), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_ListWithLowercaseStatus_NormalizesStatus()
    {
        // Arrange
        var sut = new ContactRequestQueryService(_repository.Object);

        // Act
        await sut.Handle(new ListContactRequestsQuery(" notified ", 5, 10), CancellationToken.None);

        // Assert
        Assert.Equal(new object?[] { ContactRequest.Notified, 5, 10, CancellationToken.None }, _repository.Calls.Single().Args);
    }
}
