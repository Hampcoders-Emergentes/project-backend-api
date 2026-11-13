using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Commands;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Queries;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.IAM.Interfaces.ACL.Services;
using Hampcoders.Electrolink.API.IAM.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.IAM.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using static Electrolink.Backend.UnitTests.IAM.IamTestData;

namespace Electrolink.Backend.UnitTests.IAM.Interfaces.ACL.Services;

public class IamContextFacadeTests
{
    private readonly Fake<IUserQueryService> _users = new();

    [Fact]
    public async Task FetchUserIdByEmail_WhenUserExists_ReturnsUserId()
    {
        // Arrange
        var user = NewUser();
        _users.Returns(nameof(IUserQueryService.Handle), user);
        var sut = new IamContextFacade(_users.Object);

        // Act
        var result = await sut.FetchUserIdByEmail(UserEmail);

        // Assert
        Assert.Equal(user.Id.Value, result);
        Assert.IsType<GetUserByEmailQuery>(_users.Calls.Single().Args[0]);
    }

    [Fact]
    public async Task FetchUserIdByEmail_WhenUserMissing_ReturnsEmptyString()
    {
        // Arrange
        var sut = new IamContextFacade(_users.Object);

        // Act
        var result = await sut.FetchUserIdByEmail(UserEmail);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task FetchEmailByUserId_WhenUserExists_ReturnsEmail()
    {
        // Arrange
        _users.Returns(nameof(IUserQueryService.Handle), NewUser());
        var sut = new IamContextFacade(_users.Object);

        // Act
        var result = await sut.FetchEmailByUserId(Ids.User);

        // Assert
        Assert.Equal(UserEmail, result);
    }

    [Fact]
    public async Task FetchEmailByUserId_WhenUserMissing_ReturnsEmptyString()
    {
        // Arrange
        var sut = new IamContextFacade(_users.Object);

        // Act
        var result = await sut.FetchEmailByUserId(Ids.User);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UserExistsAsync_DependingOnLookup_ReturnsWhetherUserWasFound(bool exists)
    {
        // Arrange
        if (exists) _users.Returns(nameof(IUserQueryService.Handle), NewUser());
        var sut = new IamContextFacade(_users.Object);

        // Act
        var result = await sut.UserExistsAsync(Ids.User);

        // Assert
        Assert.Equal(exists, result);
    }

    [Fact]
    public async Task GetUserAccessRoleAsync_WhenUserExists_ReturnsRoleName()
    {
        // Arrange
        _users.Returns(nameof(IUserQueryService.Handle), NewUser(AccessRole.Maker));
        var sut = new IamContextFacade(_users.Object);

        // Act
        var result = await sut.GetUserAccessRoleAsync(Ids.User);

        // Assert
        Assert.Equal("Maker", result);
    }

    [Fact]
    public async Task GetUserAccessRoleAsync_WhenUserMissing_ReturnsNull()
    {
        // Arrange
        var sut = new IamContextFacade(_users.Object);

        // Act
        var result = await sut.GetUserAccessRoleAsync(Ids.User);

        // Assert
        Assert.Null(result);
    }
}
