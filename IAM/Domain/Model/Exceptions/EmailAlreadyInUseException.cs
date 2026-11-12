using System.Runtime.Serialization;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;

namespace Hampcoders.Electrolink.API.IAM.Domain.Model.Exceptions;

[Serializable]
public class EmailAlreadyInUseException : Exception, ICodedError
{
    public string Email { get; } = string.Empty;
    public string ErrorCode => ErrorCodes.EmailAlreadyRegistered;

    public EmailAlreadyInUseException()
    { }

    public EmailAlreadyInUseException(Email? email)
        : base("The email address is already registered.")
    {
        Email = email?.Value ?? string.Empty;
    }

    public EmailAlreadyInUseException(string? message, Exception? innerException)
        : base(message, innerException)
    { }

    protected EmailAlreadyInUseException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        Email = info.GetString(nameof(Email)) ?? string.Empty;
    }

    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        if (info is null) throw new ArgumentNullException(nameof(info));
        info.AddValue(nameof(Email), Email);
        base.GetObjectData(info, context);
    }
}