using Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;

namespace Hampcoders.Electrolink.API.Shared.Interfaces.REST;

/// <summary>
///     Controller error bodies understood by the unified error filter: <c>message</c> becomes <c>detail</c>,
///     <c>errorCode</c> the envelope's code and <c>errors</c> its field-level code lists.
/// </summary>
public static class ErrorBodies
{
    public static object Coded(string message, string errorCode) => new { message, errorCode };

    public static object Field(string message, string errorCode, string field, string fieldCode) => new
    {
        message,
        errorCode,
        errors = (IDictionary<string, string[]>)new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [fieldCode] }
    };

    public static object Field(FieldValidationException e) => Field(e.Message, e.ErrorCode, e.Field, e.FieldCode);

    public static object EmailAlreadyRegistered() => Field("The email address is already registered.", ErrorCodes.EmailAlreadyRegistered,
        "email", ErrorCodes.EmailAlreadyRegistered);

    public static object InvalidEmail() => Field("The email address is not valid.", ErrorCodes.ValidationFailed,
        "email", ErrorCodes.FieldInvalidEmail);

    public static object AccountNotActive() => Coded("The account is not active.", ErrorCodes.AccountNotActive);
}
