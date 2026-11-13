using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.IAM.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Commands;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Entities;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Events;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.IAM.Domain.Repositories;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using static Electrolink.Backend.UnitTests.IAM.IamTestData;

namespace Electrolink.Backend.UnitTests.IAM.Application.Internal.CommandServices;

public class UserCommandServiceTests
{
    private readonly Fake<IUserRepository> _users = new();
    private readonly Fake<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Fake<ITokenService> _tokens = Tokens();
    private readonly Fake<IMediator> _mediator = new();
    private readonly Fake<IProfilesContextFacade> _profiles = new();

    private UserCommandService CreateSut() => new(_users.Object, _refreshTokens.Object, _tokens.Object, Hashing().Object,
        new Fake<IUnitOfWork>().Object, _mediator.Object, NullLogger<UserCommandService>.Instance, new ExternalProfilesService(_profiles.Object));

    private User GivenUserByEmail()
    {
        var user = NewUser();
        _users.Returns(nameof(IUserRepository.FindByEmailAsync), user);
        return user;
    }

    private User GivenUserById()
    {
        var user = NewUser();
        _users.Returns(nameof(IUserRepository.FindByIdAsync), user);
        return user;
    }

    private RefreshToken GivenStoredRefreshToken(User user, DateTime? issuedAt = null)
    {
        var stored = RefreshToken.Issue(user.Id, "h(old)", TimeSpan.FromDays(7), issuedAt ?? DateTime.UtcNow);
        _refreshTokens.Returns(nameof(IRefreshTokenRepository.FindByHashAsync), stored);
        return stored;
    }

    [Fact]
    public async Task Handle_SignInWithUnknownEmail_ThrowsInvalidCredentialsException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new SignInCommand(UserEmail, "secret"));

        // Assert
        await Assert.ThrowsAsync<InvalidCredentialsException>(act);
    }

    [Fact]
    public async Task Handle_SignInWithWrongPassword_ThrowsInvalidCredentialsException()
    {
        // Arrange
        GivenUserByEmail();
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new SignInCommand(UserEmail, "wrong"));

        // Assert
        await Assert.ThrowsAsync<InvalidCredentialsException>(act);
    }

    [Fact]
    public async Task Handle_SignInWithSuspendedAccount_ThrowsInactiveAccountException()
    {
        // Arrange
        GivenUserByEmail().Suspend("fraud");
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new SignInCommand(UserEmail, "secret"));

        // Assert
        await Assert.ThrowsAsync<InactiveAccountException>(act);
    }

    [Fact]
    public async Task Handle_SignInWithValidCredentials_ReturnsTokens()
    {
        // Arrange
        var user = GivenUserByEmail();
        var sut = CreateSut();

        // Act
        var (resultUser, token, refreshToken) = await sut.Handle(new SignInCommand(UserEmail, "secret"));

        // Assert
        Assert.Equal((user, "jwt-" + user.Id.Value, "refresh-1"), (resultUser, token, refreshToken));
    }

    [Fact]
    public async Task Handle_SignInWithValidCredentials_PersistsHashedRefreshToken()
    {
        // Arrange
        GivenUserByEmail();
        var sut = CreateSut();

        // Act
        await sut.Handle(new SignInCommand(UserEmail, "secret"));

        // Assert
        Assert.Equal("h(refresh-1)", _refreshTokens.CallsTo(nameof(IRefreshTokenRepository.AddAsync)).Single().Arg<RefreshToken>().TokenHash);
    }

    [Fact]
    public async Task Handle_SignInWithValidCredentials_PublishesSignedInEvent()
    {
        // Arrange
        GivenUserByEmail();
        var sut = CreateSut();

        // Act
        await sut.Handle(new SignInCommand(UserEmail, "secret"));

        // Assert
        Assert.IsType<UserSignedInEvent>(_mediator.Calls.Single().Args[0]);
    }

    [Theory]
    [InlineData("", "secret1", "secret1")]
    [InlineData(UserEmail, " ", "secret1")]
    [InlineData(UserEmail, "secret1", "")]
    public async Task Handle_SignUpWithEmptyFields_ThrowsArgumentException(string email, string password, string confirmation)
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new SignUpCommand(email, password, confirmation, "User"));

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_SignUpWithMismatchedConfirmation_ThrowsPasswordRejectedException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new SignUpCommand(UserEmail, "secret1", "secret2", "User"));

        // Assert
        await Assert.ThrowsAsync<PasswordRejectedException>(act);
    }

    [Fact]
    public async Task Handle_SignUpWithTakenEmail_ThrowsEmailAlreadyInUseException()
    {
        // Arrange
        _users.Returns(nameof(IUserRepository.ExistsByEmail), true);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new SignUpCommand(UserEmail, "secret1", "secret1", "User"));

        // Assert
        await Assert.ThrowsAsync<EmailAlreadyInUseException>(act);
    }

    [Fact]
    public async Task Handle_SignUpRequestingAdminRole_ThrowsFieldValidationException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new SignUpCommand(UserEmail, "secret1", "secret1", "Admin"));

        // Assert
        await Assert.ThrowsAsync<FieldValidationException>(act);
    }

    [Fact]
    public async Task Handle_SignUpWithUnknownRole_CreatesUserRole()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var (user, _, _) = await sut.Handle(new SignUpCommand(UserEmail, "secret1", "secret1", "wizard"));

        // Assert
        Assert.Equal(AccessRole.User, user.Role);
    }

    [Fact]
    public async Task Handle_SignUpWithValidData_StoresHashedPassword()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var (user, _, _) = await sut.Handle(new SignUpCommand(UserEmail, "secret1", "secret1", "User"));

        // Assert
        Assert.Equal("hash:secret1", user.PasswordHash.Value);
        Assert.Same(user, _users.CallsTo(nameof(IUserRepository.AddAsync)).Single().Arg<User>());
    }

    [Fact]
    public async Task Handle_RefreshWithBlankToken_ThrowsInvalidRefreshTokenException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new RefreshTokenCommand(" "));

        // Assert
        await Assert.ThrowsAsync<InvalidRefreshTokenException>(act);
    }

    [Fact]
    public async Task Handle_RefreshWithUnknownToken_ThrowsInvalidRefreshTokenException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new RefreshTokenCommand("old"));

        // Assert
        await Assert.ThrowsAsync<InvalidRefreshTokenException>(act);
    }

    [Fact]
    public async Task Handle_RefreshWithRevokedToken_RevokesEveryActiveSession()
    {
        // Arrange
        var user = GivenUserById();
        GivenStoredRefreshToken(user).Revoke(DateTime.UtcNow);
        var otherSession = RefreshToken.Issue(user.Id, "h(other)", TimeSpan.FromDays(7), DateTime.UtcNow);
        _refreshTokens.Returns(nameof(IRefreshTokenRepository.ListActiveByUserAsync), new[] { otherSession });
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new RefreshTokenCommand("old"));

        // Assert
        await Assert.ThrowsAsync<InvalidRefreshTokenException>(act);
        Assert.True(otherSession.IsRevoked);
    }

    [Fact]
    public async Task Handle_RefreshWithExpiredToken_ThrowsInvalidRefreshTokenException()
    {
        // Arrange
        GivenStoredRefreshToken(GivenUserById(), DateTime.UtcNow.AddDays(-8));
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new RefreshTokenCommand("old"));

        // Assert
        await Assert.ThrowsAsync<InvalidRefreshTokenException>(act);
    }

    [Fact]
    public async Task Handle_RefreshForSuspendedUser_ThrowsInactiveAccountException()
    {
        // Arrange
        var user = GivenUserById();
        user.Suspend("fraud");
        GivenStoredRefreshToken(user);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new RefreshTokenCommand("old"));

        // Assert
        await Assert.ThrowsAsync<InactiveAccountException>(act);
    }

    [Fact]
    public async Task Handle_RefreshWithActiveToken_RotatesToken()
    {
        // Arrange
        var user = GivenUserById();
        var stored = GivenStoredRefreshToken(user);
        var sut = CreateSut();

        // Act
        var (_, accessToken, newRefresh) = await sut.Handle(new RefreshTokenCommand("old"));

        // Assert
        Assert.Equal(("jwt-" + user.Id.Value, "refresh-1"), (accessToken, newRefresh));
        Assert.Equal((true, "h(refresh-1)"), (stored.IsRevoked, stored.ReplacedByTokenHash));
    }

    [Fact]
    public async Task Handle_UpdatePasswordWhenUserMissing_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new UpdatePasswordCommand(Ids.User, "secret", "new-secret"));

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_UpdatePasswordWithWrongCurrentPassword_ThrowsInvalidCredentialsException()
    {
        // Arrange
        GivenUserById();
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new UpdatePasswordCommand(Ids.User, "wrong", "new-secret"));

        // Assert
        await Assert.ThrowsAsync<InvalidCredentialsException>(act);
    }

    [Fact]
    public async Task Handle_UpdatePasswordWithCorrectCurrentPassword_StoresNewHash()
    {
        // Arrange
        var user = GivenUserById();
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new UpdatePasswordCommand(Ids.User, "secret", "new-secret"));

        // Assert
        Assert.True(result);
        Assert.Equal("hash:new-secret", user.PasswordHash.Value);
    }

    [Fact]
    public async Task Handle_RefreshClaimsWhenUserMissing_ThrowsUserNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new RefreshClaimsCommand(Ids.Actor));

        // Assert
        await Assert.ThrowsAsync<UserNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_RefreshClaimsForActiveUser_ReturnsNewToken()
    {
        // Arrange
        var user = GivenUserById();
        var sut = CreateSut();

        // Act
        var token = await sut.Handle(new RefreshClaimsCommand(user.Id));

        // Assert
        Assert.Equal("jwt-" + user.Id.Value, token);
    }

    [Fact]
    public async Task Handle_RefreshClaimsForSuspendedUser_ThrowsInactiveAccountException()
    {
        // Arrange
        GivenUserById().Suspend("fraud");
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new RefreshClaimsCommand(Ids.Actor));

        // Assert
        await Assert.ThrowsAsync<InactiveAccountException>(act);
    }

    [Fact]
    public async Task Handle_SuspendExistingUser_MarksUserSuspended()
    {
        // Arrange
        var user = GivenUserById();
        var sut = CreateSut();

        // Act
        await sut.Handle(new SuspendUserAccountCommand(Ids.User, "fraud"));

        // Assert
        Assert.Equal(EUserStatus.Suspended, user.Status);
    }

    [Fact]
    public async Task Handle_SuspendMissingUser_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new SuspendUserAccountCommand(Ids.User, "fraud"));

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_ActivateSuspendedUser_MarksUserActive()
    {
        // Arrange
        var user = GivenUserById();
        user.Suspend("fraud");
        var sut = CreateSut();

        // Act
        await sut.Handle(new ActivateUserAccountCommand(Ids.User));

        // Assert
        Assert.Equal(EUserStatus.Active, user.Status);
    }

    [Fact]
    public async Task Handle_RequestPasswordResetForUnknownEmail_CompletesWithoutPublishing()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(new RequestPasswordResetCommand(UserEmail));

        // Assert
        Assert.Empty(_mediator.Calls);
    }

    [Fact]
    public async Task Handle_RequestPasswordResetForKnownEmail_PublishesResetRequestedEvent()
    {
        // Arrange
        var user = GivenUserByEmail();
        var sut = CreateSut();

        // Act
        await sut.Handle(new RequestPasswordResetCommand(UserEmail));

        // Assert
        Assert.IsType<PasswordResetRequestedEvent>(_mediator.Calls.Single().Args[0]);
        Assert.NotNull(user.PasswordResetToken);
    }

    [Theory]
    [InlineData("", "new-secret1", "new-secret1")]
    [InlineData("token", "short", "short")]
    public async Task Handle_ResetPasswordWithInvalidInput_ThrowsArgumentException(string token, string password, string confirmation)
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new ResetPasswordCommand(token, password, confirmation));

        // Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_ResetPasswordWithMismatchedConfirmation_ThrowsPasswordRejectedException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new ResetPasswordCommand("token", "new-secret1", "new-secret2"));

        // Assert
        await Assert.ThrowsAsync<PasswordRejectedException>(act);
    }

    [Fact]
    public async Task Handle_ResetPasswordWithUnknownToken_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new ResetPasswordCommand("token", "new-secret1", "new-secret1"));

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_ResetPasswordWithValidToken_ReplacesHashAndRevokesSessions()
    {
        // Arrange
        var user = NewUser();
        user.RequestPasswordReset();
        var secret = user.DomainEvents.OfType<PasswordResetRequestedEvent>().Single().ResetToken;
        user.ClearDomainEvents();
        _users.Returns(nameof(IUserRepository.FindByPasswordResetTokenAsync), user);
        var session = RefreshToken.Issue(user.Id, "h(s)", TimeSpan.FromDays(7), DateTime.UtcNow);
        _refreshTokens.Returns(nameof(IRefreshTokenRepository.ListActiveByUserAsync), new[] { session });
        var sut = CreateSut();

        // Act
        await sut.Handle(new ResetPasswordCommand(secret, "new-secret1", "new-secret1"));

        // Assert
        Assert.Equal(("hash:new-secret1", true), (user.PasswordHash.Value, session.IsRevoked));
    }
}

public class StaffUserCommandServiceTests
{
    private readonly Fake<IUserRepository> _users = new();
    private readonly Fake<IMediator> _mediator = new();

    private StaffUserCommandService CreateSut() => new(_users.Object, Hashing().Object, new Fake<IUnitOfWork>().Object, _mediator.Object,
        NullLogger<StaffUserCommandService>.Instance);

    private static CreateStaffUserCommand Command(string role = "Maker", string confirmation = "secret1") =>
        new(UserEmail, "secret1", confirmation, role, "Ana", "Diaz", null);

    [Fact]
    public async Task Handle_WithMismatchedConfirmation_ThrowsPasswordRejectedException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Command(confirmation: "other"));

        // Assert
        await Assert.ThrowsAsync<PasswordRejectedException>(act);
    }

    [Fact]
    public async Task Handle_WithTakenEmail_ThrowsEmailAlreadyInUseException()
    {
        // Arrange
        _users.Returns(nameof(IUserRepository.ExistsByEmail), true);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Command());

        // Assert
        await Assert.ThrowsAsync<EmailAlreadyInUseException>(act);
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Janitor")]
    public async Task Handle_WithNonStaffRole_ThrowsArgumentException(string role)
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Command(role));

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_WithStaffRole_CreatesUserWithThatRole()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var userId = await sut.Handle(Command("fieldmanager"));

        // Assert
        var user = _users.CallsTo(nameof(IUserRepository.AddAsync)).Single().Arg<User>();
        Assert.Equal((userId, AccessRole.FieldManager), (user.Id.Value, user.Role));
    }

    [Fact]
    public async Task Handle_WithStaffRole_PublishesStaffUserCreatedEvent()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(Command());

        // Assert
        Assert.Contains(_mediator.Calls, c => c.Args[0] is StaffUserCreatedEvent);
    }
}
