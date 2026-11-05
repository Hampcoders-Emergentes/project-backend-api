using System.Text;
using System.Text.Json;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Events;
using Hampcoders.Electrolink.API.Processing.Domain.Services;
using Hampcoders.Electrolink.API.Processing.Infrastructure.Interfaces.ASP.Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace Hampcoders.Electrolink.API.Processing.Infrastructure.Services;

public class MqttDeviceSubscriberService : BackgroundService, IMqttSubscriptionService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MqttDeviceSubscriberService> _logger;
    private readonly MqttConfiguration _settings;
    private IMqttClient _mqttClient;
    private int _reconnectAttempt;

    public bool IsConnected => _mqttClient?.IsConnected ?? false;

    public event Func<MqttTelemetryReceivedEventArgs, Task> OnTelemetryReceived;
    public event Func<MqttAnomalyReceivedEventArgs, Task> OnAnomalyReceived;
    public event Func<MqttCommandResponseEventArgs, Task> OnCommandResponse;
    public event Func<MqttStatusReceivedEventArgs, Task> OnStatusReceived;

    public MqttDeviceSubscriberService(
        IOptions<MqttConfiguration> settings,
        IServiceScopeFactory scopeFactory,
        ILogger<MqttDeviceSubscriberService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[MQTT] Subscriber starting. Target: {Host}:{Port}",
            _settings.Host, _settings.Port);

        var factory = new MqttClientFactory();
        _mqttClient = factory.CreateMqttClient();

        AttachHandlers();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectWithRetryAsync(stoppingToken);

                if (_mqttClient.IsConnected)
                {
                    await SubscribeToAllTopicsAsync(stoppingToken);
                    await KeepAliveAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[MQTT] Connection error. Reconnecting in {Delay}s...",
                    _settings.ReconnectDelaySeconds);
                await Task.Delay(
                    TimeSpan.FromSeconds(_settings.ReconnectDelaySeconds), stoppingToken);
            }
        }
    }

    private void AttachHandlers()
    {
        _mqttClient.ConnectedAsync += async e =>
        {
            _logger.LogInformation("[MQTT] Connected to broker at {Host}:{Port} (Client: {ClientId})",
                _settings.Host, _settings.Port, _settings.ClientId);
            _reconnectAttempt = 0;
            await Task.CompletedTask;
        };

        _mqttClient.DisconnectedAsync += async e =>
        {
            _logger.LogWarning("[MQTT] Disconnected from broker. Reason: {Reason} (Client: {ClientId})",
                e.Reason, _settings.ClientId);
            await Task.CompletedTask;
        };

        _mqttClient.ApplicationMessageReceivedAsync += async e =>
        {
            var topic = e.ApplicationMessage.Topic;
            var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

            _logger.LogDebug("[MQTT] Received on topic: {Topic}", topic);

            try
            {
                await RouteMessageAsync(topic, payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MQTT] Error processing message on topic: {Topic}", topic);
            }
        };
    }

    private async Task RouteMessageAsync(string topic, string payload)
    {
        var parts = topic.Split('/');
        if (parts.Length < 4) return;

        var deviceId = parts[2];
        var messageType = string.Join("/", parts.Skip(3));

        switch (messageType)
        {
            case "telemetry":
                var telemetry = JsonSerializer.Deserialize<IngestDeviceReadingCommand>(payload,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (telemetry != null)
                    await SendInMessageScopeAsync(telemetry);

                if (OnTelemetryReceived != null)
                    await OnTelemetryReceived.Invoke(
                        new MqttTelemetryReceivedEventArgs(deviceId, payload));
                break;

            case "anomaly":
                var anomaly = JsonSerializer.Deserialize<ReportEdgeAnomalyCommand>(payload,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (anomaly != null)
                    await SendInMessageScopeAsync(anomaly);

                if (OnAnomalyReceived != null)
                    await OnAnomalyReceived.Invoke(
                        new MqttAnomalyReceivedEventArgs(deviceId, payload));
                break;

            case "status":
                var status = JsonSerializer.Deserialize<Dictionary<string, string>>(payload,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (status != null && status.TryGetValue("status", out var deviceStatus))
                {
                    if (deviceStatus == "disconnected" || deviceStatus == "offline")
                        await SendInMessageScopeAsync(new MarkDeviceAsDisconnectedCommand(deviceId));
                }

                if (OnStatusReceived != null)
                    await OnStatusReceived.Invoke(
                        new MqttStatusReceivedEventArgs(deviceId, payload));
                break;

            case "command/response":
                var response = JsonSerializer.Deserialize<AcknowledgeRelayExecutionCommand>(payload,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (response != null)
                    await SendInMessageScopeAsync(response);

                if (OnCommandResponse != null)
                    await OnCommandResponse.Invoke(
                        new MqttCommandResponseEventArgs(deviceId, payload));
                break;
        }
    }

    // This service is a singleton, so a mediator it held directly would resolve handlers and their scoped DbContext from the root provider.
    private async Task SendInMessageScopeAsync<TResponse>(IRequest<TResponse> command)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        await mediator.Send(command);
    }

    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(_settings.Host, _settings.Port)
            .WithClientId(_settings.ClientId)
            .WithCleanSession(false)
            // MQTT 5 (the MQTTnet default) ends the session on disconnect unless an expiry is set.
            .WithSessionExpiryInterval(_settings.SessionExpirySeconds)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30));

        if (!string.IsNullOrEmpty(_settings.Username))
            options.WithCredentials(_settings.Username, _settings.Password);

        if (_settings.UseTls)
            options.WithTlsOptions(o => o
                .WithSslProtocols(System.Security.Authentication.SslProtocols.Tls12));

        var max = _settings.MaxReconnectAttempts;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _mqttClient.ConnectAsync(options.Build(), ct);
                return;
            }
            catch when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _reconnectAttempt++;
                _logger.LogWarning(ex,
                    "[MQTT] Connection attempt {Attempt} failed. Retrying in {Delay}s...",
                    _reconnectAttempt, _settings.ReconnectDelaySeconds);

                if (max > 0 && _reconnectAttempt >= max)
                    throw new InvalidOperationException(
                        $"MQTT connection failed after {max} attempts.");

                await Task.Delay(
                    TimeSpan.FromSeconds(_settings.ReconnectDelaySeconds), ct);
            }
        }
    }

    private async Task SubscribeToAllTopicsAsync(CancellationToken ct)
    {
        var qos = (MqttQualityOfServiceLevel)_settings.QoS;

        var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(f => f.WithTopic(_settings.TelemetryTopic).WithQualityOfServiceLevel(qos))
            .WithTopicFilter(f => f.WithTopic(_settings.AnomalyTopic).WithQualityOfServiceLevel(qos))
            .WithTopicFilter(f => f.WithTopic(_settings.StatusTopic).WithQualityOfServiceLevel(qos))
            .WithTopicFilter(f => f.WithTopic(_settings.CommandResponseTopic).WithQualityOfServiceLevel(qos))
            .Build();

        var result = await _mqttClient.SubscribeAsync(subscribeOptions, ct);

        foreach (var item in result.Items)
        {
            _logger.LogInformation("[MQTT] Subscribed: {Topic} | QoS: {QoS} | Code: {Code}",
                item.TopicFilter.Topic, item.TopicFilter.QualityOfServiceLevel, item.ResultCode);
        }
    }

    private async Task KeepAliveAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _mqttClient.IsConnected)
        {
            await Task.Delay(1000, ct);
        }
    }

    public async Task PublishCommandAsync(string deviceId, string payload)
    {
        if (!IsConnected)
        {
            _logger.LogWarning("[MQTT] Cannot publish to device {DeviceId}: client not connected.",
                deviceId);
            return;
        }

        var topic = $"{_settings.TopicPrefix}/{deviceId}/command";
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(payload))
            .WithQualityOfServiceLevel((MqttQualityOfServiceLevel)_settings.QoS)
            .WithRetainFlag(false)
            .Build();

        await _mqttClient.PublishAsync(message);
        _logger.LogDebug("[MQTT] Published to {Topic}: {Payload}", topic, payload);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[MQTT] Subscriber stopping...");

        if (_mqttClient?.IsConnected == true)
        {
            var disconnectOptions = new MqttClientDisconnectOptionsBuilder()
                .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                .Build();

            await _mqttClient.DisconnectAsync(disconnectOptions, cancellationToken);
        }

        _mqttClient?.Dispose();
        await base.StopAsync(cancellationToken);
    }
}
