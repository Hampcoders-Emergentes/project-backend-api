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

namespace Electrolink.Backend.UnitTests.IAM.Application.Internal.QueryServices;

public class UserQueryServiceTests
{
    private readonly Fake<IUserRepository> _users = new();

    [Fact]
    public async Task Handle_GetByIdWhenUserExists_ReturnsUser()
    {
        // Arrange
        var user = NewUser();
        _users.Returns(nameof(IUserRepository.FindByIdAsync), user);
        var sut = new UserQueryService(_users.Object);

        // Act
        var result = await sut.Handle(new GetUserByIdQuery(Ids.User));

        // Assert
        Assert.Same(user, result);
    }

    [Fact]
    public async Task Handle_GetByMalformedId_ThrowsInvalidIdException()
    {
        // Arrange
        var sut = new UserQueryService(_users.Object);

        // Act
        var act = () => sut.Handle(new GetUserByIdQuery("42"));

        // Assert
        await Assert.ThrowsAsync<InvalidIdException>(act);
    }

    [Fact]
    public async Task Handle_GetAll_ForwardsPaging()
    {
        // Arrange
        var sut = new UserQueryService(_users.Object);

        // Act
        await sut.Handle(new GetAllUsersQuery(3, 25));

        // Assert
        Assert.Equal(new object?[] { 3, 25 }, _users.Calls.Single().Args);
    }

    [Fact]
    public async Task Handle_GetCount_ReturnsRepositoryCount()
    {
        // Arrange
        _users.Returns(nameof(IUserRepository.CountAsync), 12);
        var sut = new UserQueryService(_users.Object);

        // Act
        var result = await sut.Handle(new GetUsersCountQuery());

        // Assert
        Assert.Equal(12, result);
    }

    [Fact]
    public async Task Handle_GetByEmailWhenUserMissing_ReturnsNull()
    {
        // Arrange
        var sut = new UserQueryService(_users.Object);

        // Act
        var result = await sut.Handle(new GetUserByEmailQuery(UserEmail));

        // Assert
        Assert.Null(result);
    }
}
