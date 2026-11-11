using System.Net.Mail;

namespace Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Aggregates;

/// <summary>Demo request left by a prospective customer on the public site; the sales team follows it up.</summary>
public sealed class ContactRequest
{
    public const string Received = "RECEIVED";
    public const string Notified = "NOTIFIED";

    public string Id { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Company { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string? Phone { get; private set; }
    public string Message { get; private set; } = null!;
    public string Status { get; private set; } = Received;
    public string? SourceIp { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }
    public DateTime? NotifiedAtUtc { get; private set; }
    private ContactRequest() { }

    public static ContactRequest Submit(string name, string company, string email, string? phone, string message, string? sourceIp, DateTime nowUtc)
    {
        email = (email ?? "").Trim();
        if (email.Length is 0 or > 254 || !MailAddress.TryCreate(email, out var address) || address.Address != email)
            throw new ArgumentException("email must be a valid email address.");
        phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        if (phone != null && (phone.Length > 30 || phone.Any(c => !(char.IsDigit(c) || c is '+' or ' ' or '-' or '(' or ')'))))
            throw new ArgumentException("phone may contain only digits, spaces and + - ( ) (at most 30 characters).");
        return new()
        {
            Id = "contact-" + Guid.NewGuid().ToString("N"), Name = Text(name, 120, nameof(name)), Company = Text(company, 160, nameof(company)),
            Email = email.ToLowerInvariant(), Phone = phone, Message = Text(message, 2000, nameof(message)),
            SourceIp = sourceIp is { Length: <= 45 } ? sourceIp : null, ReceivedAtUtc = nowUtc
        };
    }

    public void MarkNotified(DateTime nowUtc) { Status = Notified; NotifiedAtUtc = nowUtc; }

    private static string Text(string value, int max, string field)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0 || text.Length > max) throw new ArgumentException($"{field} is required and must contain at most {max} characters.");
        return text;
    }
}
