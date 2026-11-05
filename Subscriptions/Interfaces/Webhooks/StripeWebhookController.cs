using System.Text.Json;
using Stripe;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes;
using Hampcoders.Electrolink.API.Shared.Infrastructure;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Repository;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hampcoders.Electrolink.API.Subscriptions.Interfaces.Webhooks;

[ApiController]
[Route("api/v1/webhooks/stripe")]
public class StripeWebhookController(
    IConfiguration configuration,
    ILogger<StripeWebhookController> logger,
    ISubscriptionCommandService commandService,
    IPaymentProvider paymentProvider,
    IWebhookEventInbox webhookInbox) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Handle()
    {
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
        var signature = Request.Headers["Stripe-Signature"].ToString();
        var webhookSecret = configuration["Stripe:WebhookSecret"];

        // An HMAC keyed with an empty secret can be computed by anyone, so an unset secret must never reach verification.
        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            logger.LogError("Stripe:WebhookSecret is not configured; rejecting Stripe webhook.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (string.IsNullOrWhiteSpace(signature))
            return BadRequest(new { error = "Invalid signature" });

        WebhookEvent webhookEvent;
        try
        {
            webhookEvent = await paymentProvider.ValidateWebhookSignatureAsync(json, signature, webhookSecret);
        }
        catch (StripeException ex)
        {
            logger.LogWarning("Invalid Stripe webhook signature: {Message}", ex.Message);
            return BadRequest(new { error = "Invalid signature" });
        }
        catch (PaymentProviderException ex)
        {
            logger.LogWarning("Payment provider validation error: {Message}", ex.Message);
            return BadRequest(new { error = "Invalid signature" });
        }

        try
        {
            Func<WebhookEvent, Task> handler = webhookEvent.EventType switch
            {
                "checkout.session.completed" => HandleCheckoutSessionCompletedAsync,
                "invoice.payment_succeeded" => HandleInvoicePaymentSucceededAsync,
                "invoice.payment_failed" => HandleInvoicePaymentFailedAsync,
                "customer.subscription.deleted" => HandleCustomerSubscriptionDeletedAsync,
                _ => null!
            };

            if (handler is null)
            {
                logger.LogInformation("Unhandled Stripe event type: {EventType}", webhookEvent.EventType);
                return Ok();
            }

            var firstDelivery = await webhookInbox.ProcessOnceAsync(
                webhookEvent.EventId, webhookEvent.EventType, () => handler(webhookEvent));

            if (!firstDelivery)
                logger.LogInformation("Ignoring duplicate Stripe event {EventId} ({EventType})", webhookEvent.EventId, webhookEvent.EventType);
        }
        catch (ArgumentException ex)
        {
            logger.LogError(
                ex,
                """
                Invalid argument processing Stripe webhook.
                EventType={EventType}
                Message={Message}
                StackTrace={StackTrace}
                """,
                webhookEvent.EventType,
                ex.Message,
                ex.StackTrace);

            return BadRequest(new
            {
                EventType = webhookEvent.EventType,
                Error = ex.Message
            });
        }
        catch (PaymentProviderException ex)
        {
            logger.LogError(ex, "Payment provider error processing Stripe webhook {EventType}", webhookEvent.EventType);
            return StatusCode(502);
        }
        catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
        {
            logger.LogError(ex, "Unexpected error processing Stripe webhook {EventType}", webhookEvent.EventType);
            return StatusCode(500);
        }

        return Ok();
    }

    private async Task HandleCheckoutSessionCompletedAsync(WebhookEvent webhookEvent)
    {
        var doc = System.Text.Json.JsonDocument.Parse(webhookEvent.RawJson);
        var root = doc.RootElement;
        var dataObj = root.GetProperty("data").GetProperty("object");

        var mode = dataObj.TryGetProperty("mode", out var modeProp) ? modeProp.GetString() : null;

        if (mode == "payment")
        {
            var metadata = dataObj.TryGetProperty("metadata", out var meta) ? meta : default;
            var customerId = dataObj.TryGetProperty("customer", out var cust) ? cust.GetString()! : null;
            var amountTotal = dataObj.TryGetProperty("amount_total", out var amt) ? (int)(amt.GetInt64() / 100) : 0;
            var currency = dataObj.TryGetProperty("currency", out var cur) ? cur.GetString()! : "usd";

            var enterprisePlan = "ENTERPRISE_BASIC";
            if (metadata.ValueKind != System.Text.Json.JsonValueKind.Undefined && metadata.TryGetProperty("planType", out var pt))
                enterprisePlan = pt.GetString() ?? "ENTERPRISE_BASIC";

            await commandService.Handle(new ActivateEnterpriseSubscriptionPendingInstallationCommand(
                StripeCustomerId: customerId!,
                StripeSubscriptionId: string.Empty,
                StripeInvoiceId: $"cs_{Guid.NewGuid():N}",
                AmountPaid: amountTotal,
                Currency: currency,
                PeriodStart: DateTime.UtcNow,
                PeriodEnd: DateTime.UtcNow.AddYears(1),
                InitialDeviceCount: 0,
                PricePerDevice: 0,
                PlanType: enterprisePlan));
        }
        else if (mode == "subscription")
        {
            logger.LogInformation("Ignoring checkout.session.completed for mode=subscription — handled by invoice.payment_succeeded");
        }
        else
        {
            logger.LogWarning("Unknown checkout session mode: {Mode}", mode);
        }
    }

    private async Task HandleInvoicePaymentSucceededAsync(WebhookEvent webhookEvent)
    {
        var doc = System.Text.Json.JsonDocument.Parse(webhookEvent.RawJson);
        var root = doc.RootElement;
        var dataObj = root.GetProperty("data").GetProperty("object");

        var billingReason = dataObj.TryGetProperty("billing_reason", out var br) ? br.GetString()! : string.Empty;
        var invoiceId = dataObj.TryGetProperty("id", out var id) ? id.GetString()! : string.Empty;
        var customerId = dataObj.TryGetProperty("customer", out var cust) ? cust.GetString()! : null;
        string? subscriptionId = null;
        
        if (dataObj.TryGetProperty("subscription", out var sub) && sub.ValueKind == JsonValueKind.String)
            subscriptionId = sub.GetString();

        if (string.IsNullOrWhiteSpace(subscriptionId) &&
            dataObj.TryGetProperty("parent", out var parent) &&
            parent.TryGetProperty("subscription_details", out var subDetails) &&
            subDetails.TryGetProperty("subscription", out var parentSub) &&
            parentSub.ValueKind == JsonValueKind.String)
        {
            subscriptionId = parentSub.GetString();
        }
        
        if (dataObj.TryGetProperty("subscription", out var subscriptionElement))
        {
            logger.LogInformation(
                "Stripe subscription property type: {ValueKind}",
                subscriptionElement.ValueKind);
        }
        var amountPaid = dataObj.TryGetProperty("amount_paid", out var amt) ? (int)(amt.GetInt64() / 100) : 0;
        var currency = dataObj.TryGetProperty("currency", out var cur) ? cur.GetString()! : "usd";
        var periodStart = DateTime.UtcNow;
        var periodEnd = DateTime.UtcNow.AddMonths(1);

        if (dataObj.TryGetProperty("lines", out var lines) && lines.TryGetProperty("data", out var dataArray) && dataArray.GetArrayLength() > 0)
        {
            var line = dataArray[0];
            if (line.TryGetProperty("period", out var period))
            {
                if (period.TryGetProperty("start", out var ps) &&
                    ps.ValueKind == JsonValueKind.Number)
                {
                    periodStart = DateTimeOffset
                        .FromUnixTimeSeconds(ps.GetInt64())
                        .UtcDateTime;
                }

                if (period.TryGetProperty("end", out var pe) &&
                    pe.ValueKind == JsonValueKind.Number)
                {
                    periodEnd = DateTimeOffset
                        .FromUnixTimeSeconds(pe.GetInt64())
                        .UtcDateTime;
                }
            }
        }

        if (billingReason == "subscription_create")
        {
            var billingCycle = "MONTHLY";
            if (dataObj.TryGetProperty("lines", out var lines2) && lines2.TryGetProperty("data", out var dataArray2) && dataArray2.GetArrayLength() > 0)
            {
                var line = dataArray2[0];
                if (line.TryGetProperty("price", out var price) && price.TryGetProperty("recurring", out var recurring) && recurring.TryGetProperty("interval", out var interval))
                {
                    billingCycle = interval.GetString() == "month" ? "MONTHLY" : "ANNUAL";
                }
            }
            
            if (string.IsNullOrWhiteSpace(subscriptionId))
            {
                logger.LogError(
                    """
                    Stripe invoice.payment_succeeded arrived without subscription id.

                    InvoiceId={InvoiceId}
                    CustomerId={CustomerId}
                    Payload={Payload}
                    """,
                    invoiceId,
                    customerId,
                    dataObj.GetRawText());

                return;
            }
            
            await commandService.Handle(new ActivateSubscriptionCommand(
                StripeCustomerId: customerId!,
                StripeSubscriptionId: subscriptionId!,
                StripeInvoiceId: invoiceId,
                BillingCycle: billingCycle,
                AmountPaid: amountPaid,
                Currency: currency,
                PeriodStart: periodStart,
                PeriodEnd: periodEnd));
        }
        else if (billingReason == "subscription_cycle")
        {
            await commandService.Handle(new RecordSuccessfulRenewalCommand(
                StripeSubscriptionId: subscriptionId!,
                StripeInvoiceId: invoiceId,
                AmountPaid: amountPaid,
                Currency: currency,
                NewPeriodStart: periodStart,
                NewPeriodEnd: periodEnd));
        }
    }

    private async Task HandleInvoicePaymentFailedAsync(WebhookEvent webhookEvent)
    {
        var doc = System.Text.Json.JsonDocument.Parse(webhookEvent.RawJson);
        var root = doc.RootElement;
        var dataObj = root.GetProperty("data").GetProperty("object");

        var subscriptionId = dataObj.TryGetProperty("subscription", out var sub) ? sub.GetString()! : null;
        var invoiceId = dataObj.TryGetProperty("id", out var id) ? id.GetString()! : string.Empty;
        var amountDue = dataObj.TryGetProperty("amount_due", out var amt) ? (int)(amt.GetInt64() / 100) : 0;
        var currency = dataObj.TryGetProperty("currency", out var cur) ? cur.GetString()! : "usd";

        if (subscriptionId is null) return;

        await commandService.Handle(new StartGracePeriodCommand(
            StripeSubscriptionId: subscriptionId,
            StripeInvoiceId: invoiceId,
            AmountDue: amountDue,
            Currency: currency,
            FailedAt: DateTime.UtcNow));
    }

    private async Task HandleCustomerSubscriptionDeletedAsync(WebhookEvent webhookEvent)
    {
        var doc = System.Text.Json.JsonDocument.Parse(webhookEvent.RawJson);
        var root = doc.RootElement;
        var dataObj = root.GetProperty("data").GetProperty("object");

        var subscriptionId = dataObj.TryGetProperty("id", out var id) ? id.GetString()! : null;
        var cancellationDetails = dataObj.TryGetProperty("cancellation_details", out var cd) ? cd : default;
        var reason = cancellationDetails.ValueKind != System.Text.Json.JsonValueKind.Undefined
                     && cancellationDetails.TryGetProperty("reason", out var r)
                     ? r.GetString() == "payment_failed" ? "PAYMENT_FAILURE" : "VOLUNTARY_CANCELLATION"
                     : "VOLUNTARY_CANCELLATION";

        if (subscriptionId is null) return;

        await commandService.Handle(new DegradeSubscriptionCommand(
            StripeSubscriptionId: subscriptionId,
            Reason: reason,
            DegradedAt: DateTime.UtcNow));
    }
}
