using System.Runtime.Serialization;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;

namespace Hampcoders.Electrolink.API.IAM.Domain.Model.Exceptions;

[Serializable]
public class InvalidEmailException : Exception, ICodedError
{
    public string ErrorCode => ErrorCodes.ValidationFailed;

    public string RawValue { get; }

    public InvalidEmailException(string rawValue)
        : base("The email address is not valid.")
    {
        RawValue = rawValue;
    }

    public InvalidEmailException(string message, Exception inner) : base(message, inner) { }

    protected InvalidEmailException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
        RawValue = info.GetString(nameof(RawValue));
    }

    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        if (info == null) throw new ArgumentNullException(nameof(info));
        info.AddValue(nameof(RawValue), RawValue);
        base.GetObjectData(info, context);
    }
}
