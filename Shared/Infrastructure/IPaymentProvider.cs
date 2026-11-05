namespace Hampcoders.Electrolink.API.Shared.Infrastructure;

public record ExternalCustomerId(string Value);
public record CheckoutSessionResult(string SessionId, string CheckoutUrl);
public record EnterpriseCheckoutSessionResult(string SessionId, string CheckoutUrl);
public record SubscriptionStatus(
    string Status,
    DateTime? CurrentPeriodEnd,
    DateTime? CanceledAt,
    bool CancelAtPeriodEnd
);
public record RefundResult(string RefundId);
public record CustomerPortalUrl(string Url);
public record WebhookEvent(
    string EventId,
    string EventType,
    string? SubscriptionId,
    string? CustomerId,
    string? PaymentIntentId,
    string RawJson
);

public interface IPaymentProvider
{
    Task<ExternalCustomerId> CreateCustomerAsync(
        string email,
        string name,
        IReadOnlyDictionary<string, string>? metadata = null);

    Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        ExternalCustomerId customerId,
        string priceId,
        string successUrl,
        string cancelUrl,
        IReadOnlyDictionary<string, string>? metadata = null);

    Task<EnterpriseCheckoutSessionResult> CreateEnterpriseCheckoutSessionAsync(
        ExternalCustomerId customerId,
        decimal pricePerDevice,
        int deviceCount,
        string successUrl,
        string cancelUrl,
        IReadOnlyDictionary<string, string>? metadata = null);

    Task CancelSubscriptionAsync(string externalSubscriptionId, bool cancelAtPeriodEnd);

    Task<SubscriptionStatus> GetSubscriptionStatusAsync(string externalSubscriptionId);

    Task<RefundResult> CreateRefundAsync(string paymentIntentId, decimal? amount, string reason);

    Task<CustomerPortalUrl> OpenCustomerPortalAsync(ExternalCustomerId customerId, string returnUrl);

    Task<WebhookEvent> ValidateWebhookSignatureAsync(string payload, string signature, string secret);
}
