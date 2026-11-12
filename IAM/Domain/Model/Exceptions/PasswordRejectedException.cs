using Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;

namespace Hampcoders.Electrolink.API.IAM.Domain.Model.Exceptions;

/// <summary>The chosen password (or its confirmation) is not acceptable: <c>PASSWORD_REJECTED</c>, HTTP 400.</summary>
public sealed class PasswordRejectedException(string field, string fieldCode, string message)
    : FieldValidationException(field, fieldCode, message, ErrorCodes.PasswordRejected);
