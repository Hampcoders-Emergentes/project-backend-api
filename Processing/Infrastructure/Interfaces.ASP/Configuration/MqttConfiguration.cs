namespace Hampcoders.Electrolink.API.Processing.Infrastructure.Interfaces.ASP.Configuration;

public class MqttConfiguration
{
    public const string SectionName = "Mqtt";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public bool UseTls { get; set; }
    public string ClientId { get; set; } = "electrolink-backend";
    public uint SessionExpirySeconds { get; set; } = 86400;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string TopicPrefix { get; set; } = "electrolink/device";
    public string TelemetryTopic { get; set; } = "electrolink/device/+/telemetry";
    public string AnomalyTopic { get; set; } = "electrolink/device/+/anomaly";
    public string StatusTopic { get; set; } = "electrolink/device/+/status";
    public string CommandResponseTopic { get; set; } = "electrolink/device/+/command/response";
    public int QoS { get; set; } = 1;
    public int ReconnectDelaySeconds { get; set; } = 5;
    public int MaxReconnectAttempts { get; set; }
}
