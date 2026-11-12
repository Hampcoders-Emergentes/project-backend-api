using Hampcoders.Electrolink.API.IAM.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Commands;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Events;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.IAM.Domain.Repositories;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using MediatR;

namespace Hampcoders.Electrolink.API.IAM.Application.Internal.CommandServices;

public class StaffUserCommandService(
    IUserRepository userRepository,
    IHashingService hashingService,
    IUnitOfWork unitOfWork,
    IMediator mediator,
    ILogger<StaffUserCommandService> logger)
    : IStaffUserCommandService
{
    public async Task<string> Handle(CreateStaffUserCommand command)
    {
        if (command.Password != command.PasswordConfirmation)
            throw new PasswordRejectedException("passwordConfirmation", ErrorCodes.PasswordsDoNotMatch, "Password and confirmation do not match.");

        if (await userRepository.ExistsByEmail(command.Email))
            throw new EmailAlreadyInUseException(Email.From(command.Email));

        if (!Enum.TryParse<AccessRole>(command.AccessRole, ignoreCase: true, out var accessRole)
            || accessRole == AccessRole.User)
            throw new ArgumentException("Staff user must have a valid staff AccessRole (Admin, Maker, FieldManager).");

        var email = Email.From(command.Email);
        var hash = HashedPassword.FromHash(hashingService.HashPassword(command.Password));
        var user = User.Create(email, hash, accessRole);

        await userRepository.AddAsync(user);
        await unitOfWork.CompleteAsync();

        foreach (var domainEvent in user.DomainEvents)
            await mediator.Publish(domainEvent, CancellationToken.None);
        user.ClearDomainEvents();

        await mediator.Publish(new StaffUserCreatedEvent(
            user.Id.Value, user.Email.Value, user.Role.ToString(), DateTime.UtcNow));

        logger.LogInformation("[IAM] Staff user created: {Email} with role {Role}", command.Email, command.AccessRole);

        return user.Id.Value;
    }
}
