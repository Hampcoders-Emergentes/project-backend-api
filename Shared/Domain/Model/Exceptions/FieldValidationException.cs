namespace Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;

/// <summary>
///     Validation failure attributable to one input field. It is an <see cref="ArgumentException"/>, so every existing
///     <c>catch (ArgumentException)</c> keeps answering 400; the error envelope additionally carries
///     <c>errors: { field: [FieldCode] }</c> and <see cref="ErrorCode"/>.
/// </summary>
public class FieldValidationException(string field, string fieldCode, string message, string errorCode = ErrorCodes.ValidationFailed)
    : ArgumentException(message), ICodedError
{
    /// <summary>Request member (camelCase, as on the wire).</summary>
    public string Field { get; } = field;
    public string FieldCode { get; } = fieldCode;
    public string ErrorCode { get; } = errorCode;
}
