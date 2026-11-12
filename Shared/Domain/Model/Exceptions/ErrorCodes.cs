namespace Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;

/// <summary>
///     Stable, machine-readable error codes (UPPER_SNAKE_CASE). They travel as the <c>errorCode</c> member of every
///     <c>application/problem+json</c> response and as the values of its field-level <c>errors</c> map. Clients branch on
///     these, never on the English <c>detail</c> text. Codes are only ever added, never renamed.
/// </summary>
public static class ErrorCodes
{
    // Status-level defaults (used when nothing more specific is known).
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string Forbidden = "FORBIDDEN";
    public const string ResourceNotFound = "RESOURCE_NOT_FOUND";
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";
    public const string Conflict = "CONFLICT";
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
    public const string UnsupportedMediaType = "UNSUPPORTED_MEDIA_TYPE";
    public const string BusinessRuleViolated = "BUSINESS_RULE_VIOLATED";
    public const string RateLimited = "RATE_LIMITED";
    public const string InternalError = "INTERNAL_ERROR";
    public const string UpstreamFailure = "UPSTREAM_FAILURE";
    public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";
    public const string RequestFailed = "REQUEST_FAILED";

    // Specific codes.
    public const string EmailAlreadyRegistered = "EMAIL_ALREADY_REGISTERED";
    public const string PasswordRejected = "PASSWORD_REJECTED";
    public const string AccountNotActive = "ACCOUNT_NOT_ACTIVE";
    public const string StoreNotFound = "STORE_NOT_FOUND";
    public const string StaleState = "STALE_STATE";
    public const string ReportInProgress = "REPORT_IN_PROGRESS";

    // Field-level codes (values of the "errors" map).
    public const string FieldRequired = "REQUIRED";
    public const string FieldInvalid = "INVALID";
    public const string FieldInvalidFormat = "INVALID_FORMAT";
    public const string FieldInvalidEmail = "INVALID_EMAIL";
    public const string FieldTooShort = "TOO_SHORT";
    public const string FieldTooLong = "TOO_LONG";
    public const string FieldOutOfRange = "OUT_OF_RANGE";
    public const string FieldMismatch = "MISMATCH";
    public const string FieldUnknownMember = "UNKNOWN_MEMBER";
    public const string PasswordsDoNotMatch = "PASSWORDS_DO_NOT_MATCH";
    public const string PasswordLength = "PASSWORD_LENGTH";
    public const string RoleNotAllowed = "ROLE_NOT_ALLOWED";

    /// <summary>Default code for an HTTP status.</summary>
    public static string ForStatus(int status) => status switch
    {
        400 => ValidationFailed,
        401 => Unauthenticated,
        403 => Forbidden,
        404 => ResourceNotFound,
        405 => MethodNotAllowed,
        409 => Conflict,
        413 => PayloadTooLarge,
        415 => UnsupportedMediaType,
        422 => BusinessRuleViolated,
        429 => RateLimited,
        500 => InternalError,
        502 => UpstreamFailure,
        503 => ServiceUnavailable,
        _ => status >= 500 ? InternalError : RequestFailed
    };

    /// <summary>True when the text already is a code (UPPER_SNAKE_CASE).</summary>
    public static bool IsCode(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 || !char.IsAsciiLetterUpper(value[0])) return false;
        foreach (var c in value)
            if (!(char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_')) return false;
        return true;
    }
}

/// <summary>An exception that knows the stable <see cref="ErrorCodes"/> value it maps to.</summary>
public interface ICodedError
{
    string ErrorCode { get; }
}
