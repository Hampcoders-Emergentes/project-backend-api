using Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Commands;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.IAM.Domain.Repositories;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using MediatR;

namespace Hampcoders.Electrolink.API.IAM.Application.Internal.CommandServices;

/**
 * <summary>
 *     The user command service
 * </summary>
 * <remarks>
 *     This class is used to handle user commands
 * </remarks>
 */
public class UserCommandService(
    IUserRepository userRepository,
    ITokenService tokenService,
    IHashingService hashingService,
    IUnitOfWork unitOfWork,
    IMediator mediator, 
    ILogger<UserCommandService> logger,
    ExternalProfilesService externalProfilesService)
    : IUserCommandService
{
    /**
     * <summary>
     *     Handle sign in command
     * </summary>
     * <param name="command">The sign in command</param>
     * <returns>The authenticated user and the JWT token</returns>
     */
    public async Task<(User user, string token)> Handle(SignInCommand command)
    {
        var user = await userRepository.FindByEmailAsync(command.Email) ?? throw new InvalidCredentialsException("Invalid username or password");

        if (!hashingService.VerifyPassword(command.Password, user.PasswordHash.Value))
        {
            logger.LogWarning("[IAM BC] Intento de inicio de sesión fallido para {Email}: contraseña inválida.", command.Email);
            throw new InvalidCredentialsException("Invalid username or password");
        }
        // Checked after the password, so the account status is only revealed with valid credentials (M3.28).
        if (user.Status != EUserStatus.Active) throw new InactiveAccountException();
        var profileClaims = await externalProfilesService.GetProfileClaimsAsync(user.Id.Value);

        var token = tokenService.GenerateToken(user, profileClaims);

        user.RecordSignIn();

        logger.LogInformation("[IAM BC] Publicando {EventCount} evento(s) de dominio después del inicio de sesión.", user.DomainEvents.Count);
        foreach (var domainEvent in user.DomainEvents)
        {
            await mediator.Publish(domainEvent, CancellationToken.None);
        }

        user.ClearDomainEvents();

        logger.LogInformation("[IAM BC] Usuario {Email} inició sesión exitosamente.", command.Email);
        return (user, token);
    }

    /**
     * <summary>
     *     Handle sign-up command
     * </summary>
     * <param name="command">The sign-up command</param>
     * <returns>A confirmation message on successful creation.</returns>
     */
    public async Task<(User user, string token)> Handle(SignUpCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Email) || string.IsNullOrWhiteSpace(command.Password) || string.IsNullOrWhiteSpace(command.PasswordConfirmation))
            throw new ArgumentException("Username and passwords cannot be empty");
        
        if (command.Password != command.PasswordConfirmation)
            throw new ArgumentException("Password and confirmation do not match.");
        
        if (await userRepository.ExistsByEmail(command.Email))
            throw new EmailAlreadyInUseException(Email.From(command.Email));
        
        var hash = HashedPassword.FromHash(hashingService.HashPassword(command.Password));
        var role = Enum.TryParse<AccessRole>(command.Role, ignoreCase: true, out var parsedRole)
            ? parsedRole
            : AccessRole.User;

        if (role != AccessRole.User)
            throw new InvalidOperationException("Public registration only allows User role.");

        var user = User.Create(Email.From(command.Email), hash, role);
        
        await userRepository.AddAsync(user);
        await unitOfWork.CompleteAsync();
        
        foreach (var domainEvent in user.DomainEvents)
            await mediator.Publish(domainEvent, CancellationToken.None);
        user.ClearDomainEvents();
        
        var profileClaims = await externalProfilesService.GetProfileClaimsAsync(user.Id.Value);
        var token = tokenService.GenerateToken(user, profileClaims);

        return (user, token);
    }

    public async Task<bool> Handle(UpdatePasswordCommand command)
    {
        var user = await userRepository.FindByIdAsync(UserId.From(command.UserId));
        if (user == null) throw new ArgumentException("User not found.");

        if (!hashingService.VerifyPassword(command.CurrentPassword, user.PasswordHash.Value))
        {
            logger.LogWarning("Invalid current password for user {UserId}", command.UserId);
            throw new InvalidCredentialsException("Current password is incorrect.");
        }

        var newHash = HashedPassword.FromHash(hashingService.HashPassword(command.NewPassword));
        user.UpdatePasswordHash(newHash);
        await unitOfWork.CompleteAsync();

        logger.LogInformation("[IAM BC] Publicando {EventCount} evento(s) de dominio después de actualizar contraseña.", user.DomainEvents.Count);
        foreach (var domainEvent in user.DomainEvents)
        {
            await mediator.Publish(domainEvent, CancellationToken.None);
        }
        user.ClearDomainEvents();

        logger.LogInformation("[IAM BC] Contraseña del usuario {UserId} actualizada.", command.UserId);
        return true;
    }

    public async Task<string> Handle(RefreshClaimsCommand command)
    {
        var user = await userRepository.FindByIdAsync(command.UserId) ?? throw new UserNotFoundException("User not found");
        if (user.Status != EUserStatus.Active) throw new InactiveAccountException();

        var profileClaims = await externalProfilesService.GetProfileClaimsAsync(user.Id.Value);
        return tokenService.GenerateToken(user, profileClaims);
    }

    public async Task Handle(SuspendUserAccountCommand command)
    {
        var user = await userRepository.FindByIdAsync(UserId.From(command.UserId))
            ?? throw new ArgumentException($"User {command.UserId} not found.");

        user.Suspend(command.Reason);
        userRepository.Update(user);
        await unitOfWork.CompleteAsync();

        foreach (var domainEvent in user.DomainEvents)
            await mediator.Publish(domainEvent);
        user.ClearDomainEvents();

        logger.LogInformation("[IAM] User {UserId} suspended: {Reason}", command.UserId, command.Reason);
    }

    public async Task Handle(ActivateUserAccountCommand command)
    {
        var user = await userRepository.FindByIdAsync(UserId.From(command.UserId))
            ?? throw new ArgumentException($"User {command.UserId} not found.");

        user.Activate();
        userRepository.Update(user);
        await unitOfWork.CompleteAsync();

        foreach (var domainEvent in user.DomainEvents)
            await mediator.Publish(domainEvent);
        user.ClearDomainEvents();

        logger.LogInformation("[IAM] User {UserId} activated", command.UserId);
    }

    public async Task Handle(RequestPasswordResetCommand command)
    {
        var user = await userRepository.FindByEmailAsync(command.Email)
            ?? throw new ArgumentException("User not found for the given email.");

        user.RequestPasswordReset();
        userRepository.Update(user);
        await unitOfWork.CompleteAsync();

        foreach (var domainEvent in user.DomainEvents)
            await mediator.Publish(domainEvent);
        user.ClearDomainEvents();

        logger.LogInformation("[IAM] Password reset requested for {Email}", command.Email);
    }

    public async Task Handle(ResetPasswordCommand command)
    {
        if (command.NewPassword != command.NewPasswordConfirmation)
            throw new ArgumentException("Passwords do not match.");

        var user = await userRepository.FindByPasswordResetTokenAsync(command.ResetToken)
            ?? throw new ArgumentException("Invalid or expired reset token.");

        var newHash = hashingService.HashPassword(command.NewPassword);
        user.ResetPassword(command.ResetToken, newHash);
        userRepository.Update(user);
        await unitOfWork.CompleteAsync();

        foreach (var domainEvent in user.DomainEvents)
            await mediator.Publish(domainEvent);
        user.ClearDomainEvents();

        logger.LogInformation("[IAM] Password reset completed for user {UserId}", user.Id.Value);
    }
}