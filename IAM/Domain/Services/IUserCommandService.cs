using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Commands;

namespace Hampcoders.Electrolink.API.IAM.Domain.Services;

/**
 * <summary>
 *     The user command service
 * </summary>
 * <remarks>
 *     This interface is used to handle user commands
 * </remarks>
 */
public interface IUserCommandService
{
    /**
        * <summary>
        *     Handle sign in command
        * </summary>
        * <param name="command">The sign in command</param>
        * <returns>The authenticated user and the JWT token</returns>
        */
    Task<(User user, string token, string refreshToken)> Handle(SignInCommand command);

    /**
        * <summary>
        *     Handle sign up command
        * </summary>
        * <param name="command">The sign up command</param>
        * <returns>A confirmation message on successful creation.</returns>
        */
    Task<(User user, string token, string refreshToken)> Handle(SignUpCommand command);

    /**
     * <summary>
     *     Handle update password command
     * </summary>
     * <param name="command">The update password command</param>
     * <returns>True if the password was updated successfully, otherwise false.</returns>
     */
    Task<bool> Handle(UpdatePasswordCommand command);

    /**
     * <summary>
     *     Handle refresh claims command
     * </summary>
     * <param name="command">The refresh claims command</param>
     * <returns>The new JWT token with refreshed claims.</returns>
     */
    Task<string> Handle(RefreshClaimsCommand command);
    Task<(User user, string token, string refreshToken)> Handle(RefreshTokenCommand command);
    Task Handle(SuspendUserAccountCommand command);
    Task Handle(ActivateUserAccountCommand command);
    Task Handle(RequestPasswordResetCommand command);
    Task Handle(ResetPasswordCommand command);
}