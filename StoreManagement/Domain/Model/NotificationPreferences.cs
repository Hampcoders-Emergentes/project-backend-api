using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Hampcoders.Electrolink.API.StoreManagement.Domain.Model;

public static class NotificationChannel
{
    public const string Email = "EMAIL";
    public const string Sms = "SMS";
    public const string DashboardPush = "DASHBOARD_PUSH";
    /// <summary>HTTPS endpoint of a third-party system (ticketing, monitoring centre) receiving the alert as JSON.</summary>
    public const string Webhook = "WEBHOOK";
    public static readonly IReadOnlyList<string> All = [Email, Sms, DashboardPush, Webhook];
}

/// <summary>Alert severities a route subscribes to (same vocabulary as the alert severity).</summary>
public static class AlertUrgency
{
    public const string Critical = "CRITICAL";
    public const string NonCritical = "NON_CRITICAL";
    public static readonly IReadOnlyList<string> All = [Critical, NonCritical];
}

public static class NotificationRecipientRole
{
    public const string StoreManager = "STORE_MANAGER";
    public const string OperationalStaff = "OPERATIONAL_STAFF";
    public static readonly IReadOnlyList<string> All = [StoreManager, OperationalStaff];
}

/// <summary>Webhook destinations must be public HTTPS endpoints (no loopback, private or link-local hosts).</summary>
public static class WebhookDestination
{
    public static bool IsAcceptable(string url)
    {
        if (url.Length > 254 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        if (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!System.Net.IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip)) return true;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal || System.Net.IPAddress.IsLoopback(ip));
        var b = ip.GetAddressBytes();
        return !(b[0] is 10 or 127 or 0 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168)
                 || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] is >= 64 and <= 127));
    }
}

/// <summary>
///     One alert route of a store: who receives which alert severities through which channel.
///     DASHBOARD_PUSH routes target the in-app realtime feed and carry no destination; WEBHOOK routes post JSON to a
///     third-party HTTPS endpoint.
/// </summary>
public sealed class StoreNotificationPreference
{
    public const int MaxRoutesPerStore = 20;

    public string Id { get; private set; } = null!;
    public string StoreId { get; private set; } = null!;
    public string Channel { get; private set; } = null!;
    public string? Destination { get; private set; }
    public string RecipientRole { get; private set; } = null!;
    public string? RecipientName { get; private set; }
    /// <summary>Comma-separated urgency codes, kept in canonical order.</summary>
    public string UrgencyLevels { get; private set; } = null!;
    public bool Enabled { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private StoreNotificationPreference() { }

    public IReadOnlyList<string> Urgencies => UrgencyLevels.Split(',', StringSplitOptions.RemoveEmptyEntries);
    public bool Covers(string severity) => Enabled && Urgencies.Contains(severity, StringComparer.Ordinal);

    public static StoreNotificationPreference Define(string storeId, string channel, string? destination, string recipientRole,
        string? recipientName, IEnumerable<string> urgencyLevels, bool enabled, DateTime nowUtc)
    {
        channel = (channel ?? "").Trim().ToUpperInvariant();
        if (!NotificationChannel.All.Contains(channel)) throw new ArgumentException("channel must be EMAIL, SMS, DASHBOARD_PUSH or WEBHOOK.");
        recipientRole = (recipientRole ?? "").Trim().ToUpperInvariant();
        if (!NotificationRecipientRole.All.Contains(recipientRole))
            throw new ArgumentException("recipientRole must be STORE_MANAGER or OPERATIONAL_STAFF.");
        var levels = (urgencyLevels ?? []).Select(l => (l ?? "").Trim().ToUpperInvariant()).Distinct().ToList();
        if (levels.Count == 0 || levels.Any(l => !AlertUrgency.All.Contains(l)))
            throw new ArgumentException("urgencyLevels must contain CRITICAL and/or NON_CRITICAL.");
        if (recipientName != null && (recipientName.Trim().Length == 0 || recipientName.Length > 120))
            throw new ArgumentException("recipientName must contain between 1 and 120 characters.");
        return new()
        {
            Id = "notifpref-" + Guid.NewGuid().ToString("N"),
            StoreId = HierarchyValue.Id(storeId, "store-"),
            Channel = channel,
            Destination = NormalizeDestination(channel, destination),
            RecipientRole = recipientRole,
            RecipientName = recipientName?.Trim(),
            UrgencyLevels = string.Join(',', AlertUrgency.All.Where(levels.Contains)),
            Enabled = enabled,
            UpdatedAtUtc = nowUtc
        };
    }

    private static string? NormalizeDestination(string channel, string? destination)
    {
        var value = destination?.Trim();
        switch (channel)
        {
            case NotificationChannel.DashboardPush:
                if (!string.IsNullOrEmpty(value)) throw new ArgumentException("DASHBOARD_PUSH routes do not take a destination.");
                return null;
            case NotificationChannel.Email:
                if (string.IsNullOrEmpty(value) || value.Length > 254 || !MailAddress.TryCreate(value, out var address) || address.Address != value)
                    throw new ArgumentException("EMAIL routes require a valid email address.");
                return value.ToLowerInvariant();
            case NotificationChannel.Webhook:
                if (string.IsNullOrEmpty(value) || !WebhookDestination.IsAcceptable(value))
                    throw new ArgumentException("WEBHOOK routes require a public https URL of at most 254 characters.");
                return value;
            default:
                // E.164: '+' followed by 8 to 15 digits.
                if (string.IsNullOrEmpty(value) || !Regex.IsMatch(value, @"\A\+[1-9]\d{7,14}\z", RegexOptions.None, TimeSpan.FromMilliseconds(50)))
                    throw new ArgumentException("SMS routes require a phone number in E.164 format (e.g. +51987654321).");
                return value;
        }
    }
}
