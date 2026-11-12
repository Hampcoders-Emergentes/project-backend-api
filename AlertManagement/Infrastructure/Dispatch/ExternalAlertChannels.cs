using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hampcoders.Electrolink.API.AlertManagement.Application;
using Hampcoders.Electrolink.API.Shared.Application.Notifications;
using Microsoft.Extensions.Options;

namespace Hampcoders.Electrolink.API.AlertManagement.Infrastructure.Dispatch;

/// <summary>Bound from "Notifications": third-party delivery providers for alert channels.</summary>
public sealed class AlertProviderSettings
{
    public SmsGatewaySettings Sms { get; set; } = new();
    public WebhookSettings Webhook { get; set; } = new();

    /// <summary>HTTP SMS gateway: POST {"to","message","sender"} with a bearer API key.</summary>
    public sealed class SmsGatewaySettings
    {
        /// <summary>Gateway endpoint (https). Without it SMS are written to the log instead of sent.</summary>
        public string? GatewayUrl { get; set; }
        public string? ApiKey { get; set; }
        public string Sender { get; set; } = "Electrolink";
        public int TimeoutSeconds { get; set; } = 10;
    }

    public sealed class WebhookSettings
    {
        /// <summary>When set, each payload is signed: X-Electrolink-Signature: sha256=HMAC_SHA256(secret, body).</summary>
        public string? SigningSecret { get; set; }
        public int TimeoutSeconds { get; set; } = 10;
    }
}

public sealed class EmailAlertChannel(IEmailSender email) : IExternalAlertChannel
{
    public string Channel => "EMAIL";
    public Task DeliverAsync(string destination, ExternalAlertMessage message, CancellationToken ct) =>
        email.SendAsync(new EmailMessage(destination, message.Subject, message.Body), ct);
}

/// <summary>
///     SMS fallback used when no SMS gateway is configured: messages are written to the log (phone number masked).
///     Configure Notifications:Sms:GatewayUrl to deliver for real through <see cref="HttpSmsGatewayAlertChannel"/>.
/// </summary>
public sealed class LoggingSmsAlertChannel(ILogger<LoggingSmsAlertChannel> logger) : IExternalAlertChannel
{
    public string Channel => "SMS";
    public Task DeliverAsync(string destination, ExternalAlertMessage message, CancellationToken ct)
    {
        logger.LogWarning("[Alerts] SMS to {Phone} (no SMS provider configured): {Subject}", Mask(destination), message.Subject);
        return Task.CompletedTask;
    }

    internal static string Mask(string phone) => phone.Length > 4 ? new string('*', phone.Length - 4) + phone[^4..] : "****";
}

/// <summary>Delivers SMS through a third-party HTTP gateway. Non-success responses are retried by the dispatch worker.</summary>
public sealed class HttpSmsGatewayAlertChannel(IHttpClientFactory http, IOptions<AlertProviderSettings> settings,
    ILogger<HttpSmsGatewayAlertChannel> logger) : IExternalAlertChannel
{
    private const int MaxSmsLength = 320;
    public string Channel => "SMS";

    public async Task DeliverAsync(string destination, ExternalAlertMessage message, CancellationToken ct)
    {
        var sms = settings.Value.Sms;
        var client = http.CreateClient(nameof(HttpSmsGatewayAlertChannel));
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(sms.TimeoutSeconds, 1, 60));
        using var request = new HttpRequestMessage(HttpMethod.Post, sms.GatewayUrl)
        {
            Content = JsonContent.Create(new { to = destination, message = Shorten(message.Subject), sender = sms.Sender })
        };
        if (!string.IsNullOrEmpty(sms.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sms.ApiKey);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"SMS gateway answered {(int)response.StatusCode}.");
        logger.LogInformation("[Alerts] SMS sent to {Phone}", LoggingSmsAlertChannel.Mask(destination));
    }

    private static string Shorten(string text) => text.Length <= MaxSmsLength ? text : text[..(MaxSmsLength - 3)] + "...";
}

/// <summary>
///     Posts the alert as JSON to a third-party HTTPS endpoint (ticketing, monitoring centre). Redirects are not followed.
///     Payload: { event: "alert.raised", alert: {...}, subject, text }.
/// </summary>
public sealed class WebhookAlertChannel(IHttpClientFactory http, IOptions<AlertProviderSettings> settings) : IExternalAlertChannel
{
    public const string ClientName = nameof(WebhookAlertChannel);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public string Channel => "WEBHOOK";

    public async Task DeliverAsync(string destination, ExternalAlertMessage message, CancellationToken ct)
    {
        // Re-checked at delivery time: configuration or stored routes may predate the destination rules.
        if (!Hampcoders.Electrolink.API.StoreManagement.Domain.Model.WebhookDestination.IsAcceptable(destination))
            throw new InvalidOperationException("Webhook destination is not a public https URL.");
        var options = settings.Value.Webhook;
        var body = JsonSerializer.SerializeToUtf8Bytes(new { @event = "alert.raised", alert = message.Details, subject = message.Subject, text = message.Body }, Json);
        var client = http.CreateClient(ClientName);
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 60));
        using var request = new HttpRequestMessage(HttpMethod.Post, destination) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (!string.IsNullOrEmpty(options.SigningSecret))
            request.Headers.Add("X-Electrolink-Signature",
                "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.SigningSecret), body)).ToLowerInvariant());
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Webhook answered {(int)response.StatusCode}.");
    }
}
