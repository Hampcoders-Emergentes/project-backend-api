using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.IAM.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Events;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Repository;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Services;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Infrastructure;
using MediatR;
using SubscriptionStatus = Hampcoders.Electrolink.API.Subscriptions.Domain.Model.ValueObjects.SubscriptionStatus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using static Electrolink.Backend.UnitTests.Subscriptions.SubscriptionsTestData;

namespace Electrolink.Backend.UnitTests.Subscriptions.Application.Internal.CommandServices;

public class SubscriptionCommandServiceTests
{
    private readonly Fake<ISubscriptionRepository> _subscriptions = new();
    private readonly Fake<IPaymentProvider> _payments = new();
    private readonly Fake<IIamContextFacade> _iam = new Fake<IIamContextFacade>().Returns(nameof(IIamContextFacade.FetchEmailByUserId), "ana@electrolink.pe");
    private readonly Fake<ISubscriptionProfileResolver> _resolver = new();
    private readonly Fake<IMediator> _mediator = new();

    public SubscriptionCommandServiceTests()
    {
        _payments.Returns(nameof(IPaymentProvider.CreateCustomerAsync), new ExternalCustomerId(Customer));
        _resolver.Returns(nameof(ISubscriptionProfileResolver.ResolveBusinessRoleAsync), BusinessRole.Homeowner);
    }

    private SubscriptionCommandService CreateSut() => new(_subscriptions.Object, _payments.Object, new ExternalIamService(_iam.Object),
        _resolver.Object, new SubscriptionPlanPriceResolver(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Stripe:Prices:HomeownerMonthly"] = "price_home_m",
            ["Stripe:Prices:EnterpriseBasicAnnual"] = "price_ent_basic_y"
        }).Build()), Options.Create(new SubscriptionSettings()), new Fake<IUnitOfWork>().Object, _mediator.Object);

    private Subscription GivenByProfile(Subscription subscription)
    {
        _subscriptions.Returns(nameof(ISubscriptionRepository.FindByProfileIdOrFailAsync), subscription);
        return subscription;
    }

    private Subscription GivenByCustomer(Subscription subscription)
    {
        _subscriptions.Returns(nameof(ISubscriptionRepository.FindByStripeCustomerIdOrFailAsync), subscription);
        return subscription;
    }

    private Subscription GivenByStripeSubscription(Subscription subscription)
    {
        _subscriptions.Returns(nameof(ISubscriptionRepository.FindByStripeSubscriptionIdOrFailAsync), subscription);
        return subscription;
    }

    private Subscription GivenById(Subscription subscription)
    {
        _subscriptions.Returns(nameof(ISubscriptionRepository.FindByIdAsync), subscription);
        return subscription;
    }

    private static ActivateSubscriptionCommand Activate(string invoice = "in_1") => new(Customer, StripeSubscription, invoice, "MONTHLY", 1990,
        "PEN", DateTime.UtcNow, DateTime.UtcNow.AddMonths(1));

    [Fact]
    public async Task Handle_CreateWhenProfileHasSubscription_ThrowsInvalidOperationException()
    {
        // Arrange
        _subscriptions.Returns(nameof(ISubscriptionRepository.ExistsByProfileIdAsync), true);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new CreateSubscriptionCommand(UserId.From(Ids.User), Profile));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_CreateForHomeowner_InitializesBasicPlanWithUsageCounters()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var subscription = await sut.Handle(new CreateSubscriptionCommand(UserId.From(Ids.User), Profile));

        // Assert
        Assert.Equal((true, 2, Customer), (subscription.PlanType.IsBasic, subscription.UsageCounters!.MonthlyRequestsLimit,
            subscription.StripeCustomerId.Value));
        Assert.IsType<SubscriptionInitializedEvent>(_mediator.Calls.Single().Args[0]);
    }

    [Fact]
    public async Task Handle_CreateForCompany_InitializesDeviceCountWithoutCounters()
    {
        // Arrange
        _resolver.Returns(nameof(ISubscriptionProfileResolver.ResolveBusinessRoleAsync), BusinessRole.Company);
        var sut = CreateSut();

        // Act
        var subscription = await sut.Handle(new CreateSubscriptionCommand(UserId.From(Ids.User), Profile));

        // Assert
        Assert.Equal((0, (UsageCounters?)null), (subscription.ActiveDeviceCount!.Value, subscription.UsageCounters));
    }

    [Fact]
    public async Task Handle_CreateWithoutProfileName_UsesEmailLocalPartAsCustomerName()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(new CreateSubscriptionCommand(UserId.From(Ids.User), Profile));

        // Assert
        var call = _payments.CallsTo(nameof(IPaymentProvider.CreateCustomerAsync)).Single();
        Assert.Equal(("ana@electrolink.pe", "ana"), (call.Arg<string>(0), call.Arg<string>(1)));
    }

    [Fact]
    public async Task Handle_InitiateCheckoutForBasicHomeowner_ReturnsCheckoutUrlForConfiguredPrice()
    {
        // Arrange
        GivenByProfile(Basic());
        _payments.Returns(nameof(IPaymentProvider.CreateCheckoutSessionAsync), new CheckoutSessionResult("cs_1", "https://pay/cs_1"));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new InitiateCheckoutCommand(Profile.Value, "PREMIUM", "MONTHLY", "https://ok", "https://ko"));

        // Assert
        Assert.Equal(new InitiateCheckoutResult("https://pay/cs_1", "cs_1"), result);
        Assert.Equal("price_home_m", _payments.CallsTo(nameof(IPaymentProvider.CreateCheckoutSessionAsync)).Single().Arg<string>(1));
    }

    [Fact]
    public async Task Handle_InitiateCheckoutForEnterprisePlan_UsesEnterprisePrice()
    {
        // Arrange
        GivenByProfile(Basic(BusinessRole.Company));
        _payments.Returns(nameof(IPaymentProvider.CreateCheckoutSessionAsync), new CheckoutSessionResult("cs_2", "https://pay/cs_2"));
        var sut = CreateSut();

        // Act
        await sut.Handle(new InitiateCheckoutCommand(Profile.Value, "ENTERPRISE_BASIC", "ANNUAL", "https://ok", "https://ko"));

        // Assert
        Assert.Equal("price_ent_basic_y", _payments.CallsTo(nameof(IPaymentProvider.CreateCheckoutSessionAsync)).Single().Arg<string>(1));
    }

    [Fact]
    public async Task Handle_InitiateCheckoutWithUnconfiguredPrice_ThrowsInvalidOperationException()
    {
        // Arrange
        GivenByProfile(Basic());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new InitiateCheckoutCommand(Profile.Value, "PREMIUM", "ANNUAL", "https://ok", "https://ko"));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_InitiateCheckoutWithUnknownPlan_ThrowsArgumentException()
    {
        // Arrange
        GivenByProfile(Basic());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new InitiateCheckoutCommand(Profile.Value, "GOLD", "MONTHLY", "https://ok", "https://ko"));

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_ActivateBasicSubscription_UpgradesToPremiumAndRecordsPayment()
    {
        // Arrange
        var subscription = GivenByCustomer(Basic());
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(Activate());

        // Assert
        Assert.Equal((true, StripeSubscription), (result!.PlanType.IsPremium, result.StripeSubscriptionId!.Value));
        Assert.True(subscription.HasPaymentWithInvoice("in_1"));
    }

    [Fact]
    public async Task Handle_ActivateWithAlreadyRecordedInvoice_ReturnsNull()
    {
        // Arrange
        GivenByCustomer(Basic());
        var sut = CreateSut();
        await sut.Handle(Activate());

        // Act
        var result = await sut.Handle(Activate());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_ActivateEnterprisePendingInstallation_SetsPendingInstallationWithDevices()
    {
        // Arrange
        GivenByCustomer(Basic(BusinessRole.Company));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new ActivateEnterpriseSubscriptionPendingInstallationCommand(Customer, StripeSubscription, "in_2", 3000,
            "PEN", DateTime.UtcNow, DateTime.UtcNow.AddMonths(1), 3, 1000, "ENTERPRISE_BASIC"));

        // Assert
        Assert.Equal((SubscriptionStatus.PendingInstallation, 3), (result!.Status, result.ActiveDeviceCount!.Value));
    }

    [Fact]
    public async Task Handle_RecordRenewalForGracePeriod_RestoresActiveStatus()
    {
        // Arrange
        var subscription = GivenByStripeSubscription(InGracePeriod());
        var sut = CreateSut();

        // Act
        await sut.Handle(new RecordSuccessfulRenewalCommand(StripeSubscription, "in_3", 1990, "PEN", DateTime.UtcNow, DateTime.UtcNow.AddMonths(1)));

        // Assert
        Assert.Equal((SubscriptionStatus.Active, (DateTime?)null), (subscription.Status, subscription.GracePeriodEndsAt));
    }

    [Fact]
    public async Task Handle_StartGracePeriodForPremium_EntersGracePeriod()
    {
        // Arrange
        var subscription = GivenByStripeSubscription(Premium());
        var sut = CreateSut();

        // Act
        await sut.Handle(new StartGracePeriodCommand(StripeSubscription, "in_4", 1990, "PEN", DateTime.UtcNow));

        // Assert
        Assert.True(subscription.Status.IsInGracePeriod);
        Assert.NotNull(subscription.GracePeriodEndsAt);
    }

    [Fact]
    public async Task Handle_StartGracePeriodForBasic_ThrowsInvalidOperationException()
    {
        // Arrange
        GivenByStripeSubscription(Basic());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new StartGracePeriodCommand(StripeSubscription, "in_4", 1990, "PEN", DateTime.UtcNow));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_DegradeFromGracePeriod_ReturnsToBasicPlan()
    {
        // Arrange
        var subscription = GivenByStripeSubscription(InGracePeriod());
        var sut = CreateSut();

        // Act
        await sut.Handle(new DegradeSubscriptionCommand(StripeSubscription, "PAYMENT_FAILURE", DateTime.UtcNow));

        // Assert
        Assert.True(subscription.PlanType.IsBasic);
    }

    [Fact]
    public async Task Handle_DegradeActivePremium_ThrowsInvalidOperationException()
    {
        // Arrange
        GivenByStripeSubscription(Premium());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new DegradeSubscriptionCommand(StripeSubscription, "PAYMENT_FAILURE", DateTime.UtcNow));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_CancelPremium_CancelsAtPeriodEndWithProvider()
    {
        // Arrange
        var subscription = GivenByProfile(Premium());
        var sut = CreateSut();

        // Act
        await sut.Handle(new CancelSubscriptionCommand(Profile.Value, "too expensive", null));

        // Assert
        Assert.Equal(new object?[] { StripeSubscription, true }, _payments.CallsTo(nameof(IPaymentProvider.CancelSubscriptionAsync)).Single().Args);
        Assert.Equal((true, SubscriptionStatus.CancelledPending), (subscription.CancelAtPeriodEnd, subscription.Status));
    }

    [Fact]
    public async Task Handle_CancelBasic_ThrowsInvalidOperationException()
    {
        // Arrange
        GivenByProfile(Basic());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new CancelSubscriptionCommand(Profile.Value, "x", null));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_IncrementCounterForBasicHomeowner_ConsumesOneRequest()
    {
        // Arrange
        var subscription = GivenByProfile(Basic());
        var sut = CreateSut();

        // Act
        await sut.Handle(new IncrementMonthlyRequestCounterCommand(Profile.Value));

        // Assert
        Assert.Equal(1, subscription.UsageCounters!.MonthlyRequestsUsed);
    }

    [Fact]
    public async Task Handle_IncrementCounterForPremium_LeavesCountersUnchanged()
    {
        // Arrange
        var subscription = GivenByProfile(Premium());
        var sut = CreateSut();

        // Act
        await sut.Handle(new IncrementMonthlyRequestCounterCommand(Profile.Value));

        // Assert
        Assert.Null(subscription.UsageCounters);
        Assert.Equal(0, _subscriptions.CallCount(nameof(ISubscriptionRepository.Update)));
    }

    [Fact]
    public async Task Handle_ResetCounters_ResetsEveryBasicHomeowner()
    {
        // Arrange
        var first = Basic();
        first.IncrementRequestCounter();
        var second = Basic();
        second.IncrementRequestCounter();
        _subscriptions.Returns(nameof(ISubscriptionRepository.FindAllBasicHomeownersAsync), new[] { first, second });
        var sut = CreateSut();

        // Act
        await sut.Handle(new ResetMonthlyRequestCountersCommand(DateTime.UtcNow));

        // Assert
        Assert.Equal((0, 0), (first.UsageCounters!.MonthlyRequestsUsed, second.UsageCounters!.MonthlyRequestsUsed));
    }

    [Fact]
    public async Task Handle_UpdateBillingCycle_SetsNewCycle()
    {
        // Arrange
        var subscription = GivenByStripeSubscription(Premium());
        var sut = CreateSut();

        // Act
        await sut.Handle(new UpdateBillingCycleCommand(StripeSubscription, "ANNUAL", DateTime.UtcNow, DateTime.UtcNow.AddYears(1)));

        // Assert
        Assert.Equal(BillingCycle.Annual, subscription.BillingCycle);
    }

    [Fact]
    public async Task Handle_OpenCustomerPortal_ReturnsProviderUrl()
    {
        // Arrange
        GivenByProfile(Basic());
        _payments.Returns(nameof(IPaymentProvider.OpenCustomerPortalAsync), new CustomerPortalUrl("https://portal"));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new OpenCustomerPortalCommand(Profile.Value, "https://back"));

        // Assert
        Assert.Equal("https://portal", result.PortalUrl);
    }

    [Fact]
    public async Task Handle_CancelWithRefundWhenSubscriptionMissing_ThrowsKeyNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new CancelEnterpriseSubscriptionWithRefundCommand("subs-1", "x", 10, "PEN"));

        // Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_CancelWithRefundWithoutPaymentIntent_ThrowsInvalidOperationException()
    {
        // Arrange
        GivenById(EnterprisePendingInstallation());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new CancelEnterpriseSubscriptionWithRefundCommand("subs-1", "x", 10, "PEN"));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_UpdateDeviceCountForCompany_SetsActiveDevices()
    {
        // Arrange
        var subscription = GivenById(EnterprisePendingInstallation());
        var sut = CreateSut();

        // Act
        await sut.Handle(new UpdateActiveDeviceCountCommand("subs-1", 7, "assets"));

        // Assert
        Assert.Equal(7, subscription.ActiveDeviceCount);
    }

    [Fact]
    public async Task Handle_UpdateDeviceCountForHomeowner_ThrowsInvalidOperationException()
    {
        // Arrange
        GivenById(Basic());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new UpdateActiveDeviceCountCommand("subs-1", 7, "assets"));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_UpdateDeviceCountWithMalformedId_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new UpdateActiveDeviceCountCommand("sub-1", 7, "assets"));

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_RecordInstallationServiceRequest_StoresRequestId()
    {
        // Arrange
        var subscription = GivenById(EnterprisePendingInstallation());
        var sut = CreateSut();

        // Act
        await sut.Handle(new RecordInstallationServiceRequestCommand("subs-1", "req-9"));

        // Assert
        Assert.Equal("req-9", subscription.InstallationServiceRequestId);
    }
}
