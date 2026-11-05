namespace Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Entities;

// Maps the table the webhook inbox writes with an atomic insert; it is never created through the change tracker.
public class ProcessedWebhookEvent
{
    public string EventId { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public DateTime ReceivedAt { get; private set; }

    private ProcessedWebhookEvent() { }
}
