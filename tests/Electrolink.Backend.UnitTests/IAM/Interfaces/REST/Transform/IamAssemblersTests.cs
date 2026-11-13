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

namespace Electrolink.Backend.UnitTests.IAM.Interfaces.REST.Transform;

public class IamAssemblersTests
{
    [Fact]
    public void AuthenticatedUserResource_ToResourceFromEntity_MapsUserAndTokens()
    {
        // Arrange
        var user = NewUser();

        // Act
        var resource = AuthenticatedUserResourceFromEntityAssembler.ToResourceFromEntity(user, "jwt", "refresh");

        // Assert
        Assert.Equal(new AuthenticatedUserResource(user.Id.Value, UserEmail, "jwt", "refresh"), resource);
    }

    [Fact]
    public void CreateStaffUserCommand_ToCommandFromResource_MapsAllFields()
    {
        // Arrange
        var resource = new CreateStaffUserResource(UserEmail, "p1", "p1", "Maker", "Ana", "Diaz", "999");

        // Act
        var command = CreateStaffUserCommandFromResourceAssembler.ToCommandFromResource(resource);

        // Assert
        Assert.Equal(new CreateStaffUserCommand(UserEmail, "p1", "p1", "Maker", "Ana", "Diaz", "999"), command);
    }

    [Fact]
    public void RefreshClaimsCommand_ToCommandFromResource_WrapsUserId()
    {
        // Act
        var command = RefreshClaimsCommandFromResourceAssembler.ToCommandFromResource(Ids.User);

        // Assert
        Assert.Equal(UserId.From(Ids.User), command.UserId);
    }

    [Fact]
    public void RefreshClaimsCommand_ToCommandFromMalformedUserId_ThrowsInvalidIdException()
    {
        // Act
        var act = () => RefreshClaimsCommandFromResourceAssembler.ToCommandFromResource("prof-1");

        // Assert
        Assert.Throws<InvalidIdException>(act);
    }

    [Fact]
    public void RefreshedTokenResource_ToResourceFromToken_WrapsToken()
    {
        // Act
        var resource = RefreshedTokenResourceFromTokenAssembler.ToResourceFromToken("jwt");

        // Assert
        Assert.Equal("jwt", resource.Token);
    }

    [Fact]
    public void RequestPasswordResetCommand_ToCommandFromResource_MapsEmail()
    {
        // Act
        var command = RequestPasswordResetCommandFromResourceAssembler.ToCommandFromResource(new RequestPasswordResetResource(UserEmail));

        // Assert
        Assert.Equal(new RequestPasswordResetCommand(UserEmail), command);
    }

    [Fact]
    public void ResetPasswordCommand_ToCommandFromResource_MapsAllFields()
    {
        // Act
        var command = ResetPasswordCommandFromResourceAssembler.ToCommandFromResource(new ResetPasswordResource("t", "p1", "p2"));

        // Assert
        Assert.Equal(new ResetPasswordCommand("t", "p1", "p2"), command);
    }

    [Fact]
    public void SignInCommand_ToCommandFromResource_MapsCredentials()
    {
        // Act
        var command = SignInCommandFromResourceAssembler.ToCommandFromResource(new SignInResource(UserEmail, "p1"));

        // Assert
        Assert.Equal(new SignInCommand(UserEmail, "p1"), command);
    }

    [Fact]
    public void SignUpCommand_ToCommandFromResourceWithoutRole_UsesDefaultUserRole()
    {
        // Act
        var command = SignUpCommandFromResourceAssembler.ToCommandFromResource(new SignUpResource(UserEmail, "p1", "p1"));

        // Assert
        Assert.Equal(new SignUpCommand(UserEmail, "p1", "p1", "User"), command);
    }

    [Fact]
    public void StaffUserResource_ToResourceFromEntity_MapsRoleName()
    {
        // Arrange
        var user = NewUser(AccessRole.FieldManager);

        // Act
        var resource = StaffUserResourceFromEntityAssembler.ToResourceFromEntity(user);

        // Assert
        Assert.Equal(new StaffUserResource(user.Id.Value, UserEmail, "FieldManager"), resource);
    }

    [Fact]
    public void UpdatePasswordCommand_ToCommandFromResource_MapsUserAndPasswords()
    {
        // Act
        var command = UpdatePasswordCommandFromResourceAssembler.ToCommandFromResource(Ids.User, new UpdatePasswordResource("old", "new"));

        // Assert
        Assert.Equal(new UpdatePasswordCommand(Ids.User, "old", "new"), command);
    }

    [Fact]
    public void UserResource_ToResourceFromEntity_MapsIdEmailAndRole()
    {
        // Arrange
        var user = NewUser();

        // Act
        var resource = UserResourceFromEntityAssembler.ToResourceFromEntity(user);

        // Assert
        Assert.Equal(new UserResource(user.Id.Value, UserEmail, "User"), resource);
    }
}
