using System.Text.RegularExpressions;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;

namespace Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.ErrorHandling;

/// <summary>
///     Last line of defence for the human-readable members of an error response. Domain messages are often built by string
///     interpolation, so a value object can reach the client as its compiler-generated <c>ToString()</c>
///     (<c>Email { Value = a@b.c }</c>), and an exception message can carry a stack trace or namespaced type names.
///     Everything that crosses the HTTP boundary as <c>detail</c> (or as a field message) goes through here.
/// </summary>
public static partial class ProblemDetailSanitizer
{
    public const int MaxDetailLength = 500;

    /// <summary>Record <c>ToString()</c>: <c>TypeName { Member = value, Other = value }</c> with no nested braces.</summary>
    [GeneratedRegex(@"\b[A-Z][A-Za-z0-9_]*\s\{\s(?<body>[^{}]*?)\s?\}")]
    private static partial Regex RecordToString();

    [GeneratedRegex(@"(?:^|,\s)[A-Z][A-Za-z0-9_]*\s=\s")]
    private static partial Regex RecordMember();

    /// <summary>Fully qualified type names of this API and of the data-access stack.</summary>
    [GeneratedRegex(@"\b(?:Hampcoders|Microsoft\.EntityFrameworkCore|Npgsql)(?:\.[A-Za-z_][A-Za-z0-9_`]*)+")]
    private static partial Regex QualifiedTypeName();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static string? Sanitize(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return detail;
        var text = detail;

        // Stack traces and inner-exception chains never leave the server.
        foreach (var marker in new[] { "\n   at ", "\r\n   at ", " ---> ", "--- End of", "\n at " })
        {
            var index = text.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0) text = text[..index];
        }

        // Value-object ToString() -> its values. Repeat for nested records (innermost first).
        for (var i = 0; i < 5; i++)
        {
            var replaced = RecordToString().Replace(text, match =>
                string.Join(", ", RecordMember().Split(match.Groups["body"].Value).Where(v => v.Length > 0)));
            if (replaced == text) break;
            text = replaced;
        }

        text = QualifiedTypeName().Replace(text, match => match.Value[(match.Value.LastIndexOf('.') + 1)..]);
        text = Whitespace().Replace(text, " ").Trim();
        if (text.Length > MaxDetailLength) text = text[..(MaxDetailLength - 1)].TrimEnd() + "…";
        return text.Length == 0 ? null : text;
    }

    /// <summary>Maps a free-text field message (DataAnnotations, System.Text.Json, domain) to a stable field code.</summary>
    public static string ToFieldCode(string? message)
    {
        if (ErrorCodes.IsCode(message)) return message!;
        var m = (message ?? string.Empty).ToLowerInvariant();
        if (m.Length == 0) return ErrorCodes.FieldInvalid;
        if (m.Contains("could not be mapped to any .net member") || m.Contains("unmapped member")) return ErrorCodes.FieldUnknownMember;
        if (m.Contains("is required") || m.Contains("non-empty request body")) return ErrorCodes.FieldRequired;
        if (m.Contains("e-mail") || m.Contains("email address") || m.StartsWith("invalid email")) return ErrorCodes.FieldInvalidEmail;
        if (m.Contains("minimum length") && m.Contains("maximum length")) return ErrorCodes.FieldOutOfRange;
        if (m.Contains("minimum length")) return ErrorCodes.FieldTooShort;
        if (m.Contains("maximum length")) return ErrorCodes.FieldTooLong;
        if (m.Contains("must be between") || m.Contains("out of range")) return ErrorCodes.FieldOutOfRange;
        if (m.Contains("do not match") || m.Contains("must match each other")) return ErrorCodes.FieldMismatch;
        if (m.Contains("could not be converted") || m.Contains("json value") || m.Contains("regular expression")
            || m.Contains("is not valid") || m.Contains("invalid format") || m.Contains("'$") || m.Contains("path:"))
            return ErrorCodes.FieldInvalidFormat;
        return ErrorCodes.FieldInvalid;
    }
}
