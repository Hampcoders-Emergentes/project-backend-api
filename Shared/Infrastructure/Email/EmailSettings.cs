namespace Hampcoders.Electrolink.API.Shared.Infrastructure.Email;

/// <summary>Bound from the "Email" configuration section. Without Smtp:Host, mail is written to the log instead.</summary>
public sealed class EmailSettings
{
    public string FromAddress { get; set; } = "no-reply@electrolink.local";
    public string FromName { get; set; } = "Electrolink";
    public SmtpSettings Smtp { get; set; } = new();

    public sealed class SmtpSettings
    {
        public string? Host { get; set; }
        public int Port { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;
        public string? Username { get; set; }
        public string? Password { get; set; }
        public int TimeoutSeconds { get; set; } = 15;
    }
}
