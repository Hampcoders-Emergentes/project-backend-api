namespace Hampcoders.Electrolink.API.Subscriptions.Domain.Repository;

public interface IWebhookEventInbox
{
    /// <summary>
    /// Records <paramref name="eventId"/> and runs <paramref name="handler"/> as one atomic unit.
    /// If the handler fails nothing is recorded, so the provider's retry processes the event again.
    /// </summary>
    /// <returns>False, without running the handler, when the event was already processed.</returns>
    Task<bool> ProcessOnceAsync(string eventId, string eventType, Func<Task> handler);
}
