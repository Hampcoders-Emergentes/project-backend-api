using Hampcoders.Electrolink.API.IAM.Domain.Model.Events;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Events;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;

public class User : BaseAggregateRoot
{
    public UserId Id { get; private set; }
    public Email Email { get; private set; }
    public HashedPassword PasswordHash { get; private set; }
    public AccessRole Role { get; private set; }
    public EUserStatus Status { get; private set; }
    public PasswordResetToken? PasswordResetToken { get; private set; }
    private User() { }

    public static User Create(Email email, HashedPassword passwordHash, AccessRole role)
    {
        var user = new User
        {
            Id = UserId.NewUserId(),
            Email = email,
            PasswordHash = passwordHash,
            Role = role,
            Status = EUserStatus.Active
        };

        user.RaiseDomainEvent(new UserRegisteredEvent(user.Id.Value, user.Email.Value, DateTime.UtcNow, user.Role.ToString(), user.Role.ToString()));

        return user;
    }

    public void UpdatePasswordHash(HashedPassword newPasswordHash)
    {
        PasswordHash = newPasswordHash;
        RaiseDomainEvent(new UserPasswordChangedEvent(Id.Value, DateTime.UtcNow));
    }

    public void RecordSignIn()
    {
        RaiseDomainEvent(new UserSignedInEvent(Id.Value, DateTime.UtcNow));
    }

    public void Suspend(string reason)
    {
        if (Status == EUserStatus.Suspended) return;
        Status = EUserStatus.Suspended;
        RaiseDomainEvent(new UserAccountSuspendedEvent(Id.Value, reason, DateTime.UtcNow));
    }

    public void Activate()
    {
        if (Status == EUserStatus.Active) return;
        Status = EUserStatus.Active;
        RaiseDomainEvent(new UserAccountActivatedEvent(Id.Value, DateTime.UtcNow));
    }

    /// <summary>
    ///     Issues a new reset secret (replacing any previous one). The secret travels only in the raised event,
    ///     which is delivered in memory to the notifier; the aggregate keeps its hash.
    /// </summary>
    public void RequestPasswordReset()
    {
        var now = DateTime.UtcNow;
        var (token, secret) = PasswordResetToken.Issue(now);
        PasswordResetToken = token;
        RaiseDomainEvent(new PasswordResetRequestedEvent(Id.Value, Email.Value, secret, token.ExpiresAt, now));
    }

    /// <summary>Consumes the reset secret: valid once, before it expires.</summary>
    public void ResetPassword(string token, string newPasswordHash)
    {
        if (PasswordResetToken == null || !PasswordResetToken.IsValid(token, DateTime.UtcNow))
            throw new ArgumentException("Reset token is invalid or expired.");

        PasswordHash = HashedPassword.FromHash(newPasswordHash);
        PasswordResetToken = null;
        RaiseDomainEvent(new PasswordResetEvent(Id.Value, DateTime.UtcNow));
    }
}
