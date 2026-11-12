namespace Hampcoders.Electrolink.API.AlertManagement.Application;

/// <summary>How a channel behaves in this deployment.</summary>
public static class NotificationDeliveryMode
{
    /// <summary>Messages leave the system through a real provider (SMTP server, SMS gateway, SignalR hub, HTTPS webhook).</summary>
    public const string Delivered = "DELIVERED";
    /// <summary>No provider is configured: the message is only written to the server log, nobody receives it.</summary>
    public const string LogOnly = "LOG_ONLY";
}

/// <param name="Channel">EMAIL, SMS, DASHBOARD_PUSH or WEBHOOK (same vocabulary as store notification routes).</param>
/// <param name="DeliveryMode">DELIVERED or LOG_ONLY.</param>
/// <param name="Configured">True when the provider behind the channel is configured in this deployment.</param>
public sealed record NotificationChannelReadiness(string Channel, string DeliveryMode, bool Configured);

/// <summary>
///     Outbound port: inspects which delivery providers are wired in this deployment. Implementations must never expose
///     provider hostnames, URLs, keys or credentials, only whether a provider is present.
/// </summary>
public interface INotificationChannelReadinessPort
{
    IReadOnlyList<NotificationChannelReadiness> Describe();
}

public sealed record GetNotificationChannelReadinessQuery;

/// <summary>Readiness of every alert channel, in the canonical order EMAIL, SMS, DASHBOARD_PUSH, WEBHOOK.</summary>
public sealed class NotificationChannelReadinessQueryService(INotificationChannelReadinessPort port)
{
    public static readonly IReadOnlyList<string> Channels = ["EMAIL", "SMS", "DASHBOARD_PUSH", "WEBHOOK"];

    public IReadOnlyList<NotificationChannelReadiness> Handle(GetNotificationChannelReadinessQuery query)
    {
        var described = port.Describe().ToDictionary(c => c.Channel, StringComparer.Ordinal);
        // A channel the adapter does not know about is reported as not configured rather than omitted.
        return Channels.Select(channel => described.GetValueOrDefault(channel)
            ?? new NotificationChannelReadiness(channel, NotificationDeliveryMode.LogOnly, false)).ToList();
    }
}
