namespace Hampcoders.Electrolink.API.Shared.Infrastructure.ExternalProviders;

public class NullPaymentProvider : IPaymentProvider
{
    public Task<ExternalCustomerId> CreateCustomerAsync(
        string email, string name,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return Task.FromResult(new ExternalCustomerId($"null_cust_{Guid.NewGuid():N}"));
    }

    public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        ExternalCustomerId customerId, string priceId,
        string successUrl, string cancelUrl,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return Task.FromResult(new CheckoutSessionResult(
            $"null_sess_{Guid.NewGuid():N}",
            $"{successUrl}?session=null_sess_{Guid.NewGuid():N}"));
    }

    public Task<EnterpriseCheckoutSessionResult> CreateEnterpriseCheckoutSessionAsync(
        ExternalCustomerId customerId, decimal pricePerDevice, int deviceCount,
        string successUrl, string cancelUrl,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return Task.FromResult(new EnterpriseCheckoutSessionResult(
            $"null_ent_sess_{Guid.NewGuid():N}",
            $"{successUrl}?session=null_ent_sess_{Guid.NewGuid():N}"));
    }

    public Task CancelSubscriptionAsync(string externalSubscriptionId, bool cancelAtPeriodEnd)
    {
        return Task.CompletedTask;
    }

    public Task<SubscriptionStatus> GetSubscriptionStatusAsync(string externalSubscriptionId)
    {
        return Task.FromResult(new SubscriptionStatus(
            "active",
            DateTime.UtcNow.AddMonths(1),
            null,
            false));
    }

    public Task<RefundResult> CreateRefundAsync(string paymentIntentId, decimal? amount, string reason)
    {
        return Task.FromResult(new RefundResult($"null_refund_{Guid.NewGuid():N}"));
    }

    public Task<CustomerPortalUrl> OpenCustomerPortalAsync(ExternalCustomerId customerId, string returnUrl)
    {
        return Task.FromResult(new CustomerPortalUrl($"{returnUrl}?portal=null"));
    }

    public Task<WebhookEvent> ValidateWebhookSignatureAsync(string payload, string signature, string secret)
    {
        return Task.FromResult(new WebhookEvent(
            $"null_evt_{Guid.NewGuid():N}",
            "null.event",
            null,
            null,
            null,
            payload));
    }
}
