using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Services;

namespace Electrolink.Backend.UnitTests.Subscriptions;

internal static class SubscriptionsTestData
{
    public const string Customer = "cus_1";
    public const string StripeSubscription = "sub_1";
    public static readonly ProfileId Profile = ProfileId.From("prof-1");

    public static Subscription Basic(BusinessRole? role = null)
    {
        var businessRole = role ?? BusinessRole.Homeowner;
        IBillingPolicy policy = businessRole.Value == EBusinessRole.Homeowner ? new HomeownerBillingPolicy() : new EnterpriseBillingPolicy();
        var subscription = Subscription.Initialize(UserId.From(Ids.User), Profile, businessRole, StripeCustomerId.From(Customer), policy);
        subscription.ClearDomainEvents();
        return subscription;
    }

    public static Subscription Premium()
    {
        var subscription = Basic();
        subscription.Activate(StripeSubscriptionId.From(StripeSubscription), BillingCycle.Monthly,
            BillingPeriod.Of(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(29)));
        subscription.ClearDomainEvents();
        return subscription;
    }

    public static Subscription InGracePeriod()
    {
        var subscription = Premium();
        subscription.StartGracePeriod(StripeInvoiceId.From("in_fail"), DateTime.UtcNow);
        subscription.ClearDomainEvents();
        return subscription;
    }

    public static Subscription EnterprisePendingInstallation()
    {
        var subscription = Basic(BusinessRole.Company);
        subscription.ActivateEnterprisePendingInstallation(StripeSubscriptionId.From(StripeSubscription),
            BillingPeriod.Of(DateTime.UtcNow, DateTime.UtcNow.AddMonths(1)), 3, 1000, PlanType.EnterpriseBasic);
        subscription.ClearDomainEvents();
        return subscription;
    }
}
