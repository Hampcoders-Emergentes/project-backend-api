using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.ValueObjects;
using Microsoft.Extensions.Configuration;

namespace Hampcoders.Electrolink.API.Subscriptions.Domain.Services;

public class SubscriptionPlanPriceResolver
{
    private readonly IConfiguration _configuration;

    public SubscriptionPlanPriceResolver(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string ResolvePriceId(PlanType planType, BillingCycle billingCycle)
    {
        if (planType.IsBasic)
            throw new InvalidOperationException("Basic plan does not have a Stripe price ID.");

        var key = (billingCycle.Value) switch
        {
            EBillingCycle.Monthly => "Stripe:Prices:TechnicianMonthly",
            EBillingCycle.Annual => "Stripe:Prices:TechnicianAnnual",
            _ => throw new ArgumentException("Invalid billing cycle.")
        };

        return ConfiguredPrice(key);
    }

    public string ResolvePriceIdForRole(EBusinessRole role, EBillingCycle billingCycle, string? planSubtype = null)
    {
        var key = (role, billingCycle, planSubtype) switch
        {
            (EBusinessRole.Technician, EBillingCycle.Monthly, _) => "Stripe:Prices:TechnicianMonthly",
            (EBusinessRole.Technician, EBillingCycle.Annual, _) => "Stripe:Prices:TechnicianAnnual",
            (EBusinessRole.Homeowner, EBillingCycle.Monthly, _) => "Stripe:Prices:HomeownerMonthly",
            (EBusinessRole.Homeowner, EBillingCycle.Annual, _) => "Stripe:Prices:HomeownerAnnual",
            (EBusinessRole.Company, EBillingCycle.Monthly, "ENTERPRISE_BASIC") => "Stripe:Prices:EnterpriseBasicMonthly",
            (EBusinessRole.Company, EBillingCycle.Annual, "ENTERPRISE_BASIC") => "Stripe:Prices:EnterpriseBasicAnnual",
            (EBusinessRole.Company, EBillingCycle.Monthly, "ENTERPRISE_PREMIUM") => "Stripe:Prices:EnterprisePremiumMonthly",
            (EBusinessRole.Company, EBillingCycle.Annual, "ENTERPRISE_PREMIUM") => "Stripe:Prices:EnterprisePremiumAnnual",
            _ => throw new ArgumentException("Invalid role/cycle/plan combination.")
        };

        return ConfiguredPrice(key);
    }

    // appsettings declares every key, so an empty value means "not configured" rather than a price to send to Stripe.
    private string ConfiguredPrice(string key)
    {
        var priceId = _configuration[key];
        return string.IsNullOrWhiteSpace(priceId)
            ? throw new InvalidOperationException($"Stripe price is not configured for key '{key}'.")
            : priceId;
    }
}
