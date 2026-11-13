using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Events;
using Hampcoders.Electrolink.API.IAM.Interfaces.ACL;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Events;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Events;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Application.Notifications;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Repository;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Services;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.REST.Transform;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Electrolink.Backend.UnitTests.Subscriptions.SubscriptionsTestData;
using EBusinessRole = Hampcoders.Electrolink.API.Profiles.Domain.Model.ValueObjects.EBusinessRole;
using Money = Hampcoders.Electrolink.API.Subscriptions.Domain.Model.ValueObjects.Money;
using IContactRequestRepository = Hampcoders.Electrolink.API.Subscriptions.Domain.Repository.IContactRequestRepository;

namespace Electrolink.Backend.UnitTests.Subscriptions.Interfaces.REST.Transform;

public class SubscriptionsAssemblersTests
{
    [Fact]
    public void CancelSubscription_ToCommand_MapsReasonAndFeedback()
    {
        // Act
        var command = CancelSubscriptionCommandFromResourceAssembler.ToCommand("prof-1", new CancelSubscriptionResource("price", "too high"));

        // Assert
        Assert.Equal(new CancelSubscriptionCommand("prof-1", "price", "too high"), command);
    }

    [Fact]
    public void InitiateCheckout_ToCommand_MapsPlanAndUrls()
    {
        // Act
        var command = InitiateCheckoutCommandFromResourceAssembler.ToCommand("prof-1",
            new InitiateCheckoutResource("PREMIUM", "MONTHLY", "https://ok", "https://ko"));

        // Assert
        Assert.Equal(new InitiateCheckoutCommand("prof-1", "PREMIUM", "MONTHLY", "https://ok", "https://ko"), command);
    }

    [Fact]
    public void MySubscription_ToResourceForBasic_MapsCountersWithoutBillingData()
    {
        // Arrange
        var subscription = Basic();

        // Act
        var resource = MySubscriptionResourceFromEntityAssembler.ToResource(subscription);

        // Assert
        Assert.Equal(("BASIC", (string?)null, (string?)null, 0, 2), (resource.PlanType, resource.BillingCycle, resource.PeriodEnd,
            resource.MonthlyRequestsUsed!.Value, resource.MonthlyRequestsLimit!.Value));
    }

    [Fact]
    public void MySubscription_ToResourceForPremium_MapsBillingPeriodInRoundTripFormat()
    {
        // Arrange
        var subscription = Premium();

        // Act
        var resource = MySubscriptionResourceFromEntityAssembler.ToResource(subscription);

        // Assert
        Assert.Equal(subscription.BillingPeriod!.PeriodEnd.ToString("O"), resource.PeriodEnd);
        Assert.Null(resource.MonthlyRequestsUsed);
    }

    [Fact]
    public void PaymentHistory_ToResource_ConvertsCentsToDecimalAmount()
    {
        // Arrange
        var record = PaymentRecord.Create(SubscriptionId.NewSubscriptionId(), StripeInvoiceId.From("in_1"), Money.Of(1990, "PEN"),
            PaymentStatus.Succeeded, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // Act
        var resource = PaymentHistoryResourceFromEntityAssembler.ToResource([record], 2, 10);

        // Assert
        Assert.Equal((2, 10, 19.90m, "in_1"), (resource.Page, resource.PageSize, resource.Payments.Single().AmountDecimal,
            resource.Payments.Single().StripeInvoiceId));
    }

    [Fact]
    public void RequestEligibility_ToResourceWhenUpgradeRequired_IncludesUpgradePrompt()
    {
        // Act
        var resource = RequestEligibilityResourceFromEntityAssembler.ToResource(new RequestEligibilityResult(false, false, 0, "BASIC", true));

        // Assert
        Assert.NotNull(resource.UpgradePromptMessage);
    }

    [Fact]
    public void RequestEligibility_ToResourceWithoutUpgrade_OmitsUpgradePrompt()
    {
        // Act
        var resource = RequestEligibilityResourceFromEntityAssembler.ToResource(new RequestEligibilityResult(true, true, null, "PREMIUM", false));

        // Assert
        Assert.Null(resource.UpgradePromptMessage);
    }

    [Fact]
    public void SubscriptionStatusAlert_ToResourceInGracePeriod_MapsGraceDeadlineAndPortal()
    {
        // Arrange
        var subscription = InGracePeriod();

        // Act
        var resource = SubscriptionStatusAlertResourceFromEntityAssembler.ToResource(subscription, "https://portal");

        // Assert
        Assert.Equal((subscription.GracePeriodEndsAt!.Value.ToString("O"), "https://portal"), (resource.GracePeriodEndsAt, resource.CustomerPortalUrl));
    }
}
