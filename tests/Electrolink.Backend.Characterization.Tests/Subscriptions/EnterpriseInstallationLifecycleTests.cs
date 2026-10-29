using System.Reflection;
using Electrolink.Backend.Characterization.Tests.Assets;
using Hampcoders.Electrolink.API.Planning.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Events;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Repository;
using Hampcoders.Electrolink.API.Subscriptions.Infrastructure.BackgroundServices;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.ACL;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using SubscriptionBusinessRole = Hampcoders.Electrolink.API.Subscriptions.Domain.Model.ValueObjects.BusinessRole;
namespace Electrolink.Backend.Characterization.Tests.Subscriptions;

// Characterization only: current Enterprise installation lifecycle, not approved commercial rules (see tests/M3.18.md).
public sealed class EnterpriseInstallationLifecycleTests
{
    static Subscription Company() => Subscription.Initialize(UserId.From("us-synthetic"), ProfileId.From("prof-synthetic"),
        SubscriptionBusinessRole.Company, StripeCustomerId.From("cus_synthetic"), new EnterpriseBillingPolicy());

    static Subscription PendingInstallation(DateTime periodStart)
    {
        var subscription = Company();
        subscription.ActivateEnterprisePendingInstallation(StripeSubscriptionId.From("sub_synthetic"),
            BillingPeriod.Of(periodStart, periodStart.AddYears(1)), 3, 10, PlanType.EnterpriseBasic);
        subscription.ClearDomainEvents();
        return subscription;
    }

    [Fact]
    public async Task Webhook_shaped_activation_with_empty_stripe_subscription_fails_before_pending_installation()
    {
        var subscription = Company(); subscription.ClearDomainEvents();
        var saves = 0; var published = 0;
        var repository = AssignmentHttpFixture.Proxy<ISubscriptionRepository>((m, a) =>
            m.Name == nameof(ISubscriptionRepository.FindByStripeCustomerIdOrFailAsync) ? Task.FromResult(subscription) : throw new InvalidOperationException("Unexpected subscription call"));
        var unit = AssignmentHttpFixture.Proxy<IUnitOfWork>((m, a) => { saves++; return Task.CompletedTask; });
        var mediator = AssignmentHttpFixture.Proxy<IMediator>((m, a) => { published++; return Task.CompletedTask; });
        // Collaborators unused by this command are deliberately absent.
        var service = new SubscriptionCommandService(repository, null!, null!, null!, null!, null!, unit, mediator);
        // Same StripeSubscriptionId and InitialDeviceCount the checkout.session.completed (mode=payment) branch sends today.
        await Assert.ThrowsAsync<ArgumentException>(() => service.Handle(new ActivateEnterpriseSubscriptionPendingInstallationCommand(
            "cus_synthetic", string.Empty, "cs_synthetic", 100, "usd", DateTime.UtcNow, DateTime.UtcNow.AddYears(1), 0, 0, "ENTERPRISE_BASIC")));
        Assert.True(subscription.Status.IsActive); Assert.True(subscription.PlanType.IsBasic);
        Assert.Empty(subscription.DomainEvents); Assert.Equal(0, saves); Assert.Equal(0, published);
    }

    static ExternalProfilesService Profiles(string role, Action? onCall = null) => new(AssignmentHttpFixture.Proxy<IProfilesContextFacade>((m, a) =>
    {
        onCall?.Invoke();
        return m.Name switch
        {
            nameof(IProfilesContextFacade.GetProfileClaimsAsync) => Task.FromResult<(string ProfileId, string ProfileStatus, string? BusinessRole, string? RoleSubjectId, string? SubscriptionTier)?>(
                ("prof-synthetic", "Active", role, role == "Company" ? "comp-synthetic" : "ho-synthetic", null)),
            nameof(IProfilesContextFacade.GetProfileIdByCompanyIdAsync) => Task.FromResult<string?>("prof-synthetic"),
            _ => throw new InvalidOperationException("Unexpected profile call")
        };
    }));

    [Fact]
    public async Task Zero_initial_devices_creates_no_installation_request()
    {
        var calls = 0;
        var requests = AssignmentHttpFixture.Proxy<IServiceRequestRepository>((m, a) => { calls++; return Task.CompletedTask; });
        var unit = AssignmentHttpFixture.Proxy<IUnitOfWork>((m, a) => { calls++; return Task.CompletedTask; });
        var facade = AssignmentHttpFixture.Proxy<ISubscriptionContextFacade>((m, a) => { calls++; return Task.CompletedTask; });
        var handler = new EnterpriseSubscriptionPendingInstallationEventHandler(requests, unit, facade, Profiles("Company", () => calls++),
            NullLogger<EnterpriseSubscriptionPendingInstallationEventHandler>.Instance);
        await handler.Handle(new EnterpriseSubscriptionPendingInstallationEvent("sub-synthetic", "us-synthetic", "sub_synthetic", 0, 0,
            DateTime.UtcNow.AddDays(30), DateTime.UtcNow), default);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Positive_initial_devices_creates_one_company_request_carrying_the_count_and_records_it()
    {
        ServiceRequest? added = null; var saves = 0; (string, string)? recorded = null;
        var requests = AssignmentHttpFixture.Proxy<IServiceRequestRepository>((m, a) =>
        {
            if (m.Name != nameof(IServiceRequestRepository.AddAsync)) throw new InvalidOperationException("Unexpected request call");
            added = (ServiceRequest)a![0]!; return Task.CompletedTask;
        });
        var unit = AssignmentHttpFixture.Proxy<IUnitOfWork>((m, a) => { saves++; return Task.CompletedTask; });
        var facade = AssignmentHttpFixture.Proxy<ISubscriptionContextFacade>((m, a) =>
        { recorded = ((string)a![0]!, (string)a[1]!); return Task.CompletedTask; });
        var handler = new EnterpriseSubscriptionPendingInstallationEventHandler(requests, unit, facade, Profiles("Company"),
            NullLogger<EnterpriseSubscriptionPendingInstallationEventHandler>.Instance);
        await handler.Handle(new EnterpriseSubscriptionPendingInstallationEvent("sub-synthetic", "us-synthetic", "sub_synthetic", 3, 10,
            DateTime.UtcNow.AddDays(30), DateTime.UtcNow), default);
        Assert.NotNull(added); Assert.Equal(3, added.RequiredDeviceCount); Assert.Equal("us-synthetic", added.UserId);
        // M3.19: Client is the owner's Company resolved by Profiles (persistence requires it); property comes later.
        Assert.Equal(ClientIdentity.FromCompany("comp-synthetic"), added.Client); Assert.Null(added.PropertyId);
        Assert.Equal(1, saves); Assert.Equal(("sub-synthetic", added.RequestId.Value), recorded);
    }

    [Fact]
    public async Task Owner_without_active_company_profile_fails_visibly_without_creating_a_request()
    {
        var calls = 0;
        var requests = AssignmentHttpFixture.Proxy<IServiceRequestRepository>((m, a) => { calls++; return Task.CompletedTask; });
        var unit = AssignmentHttpFixture.Proxy<IUnitOfWork>((m, a) => { calls++; return Task.CompletedTask; });
        var facade = AssignmentHttpFixture.Proxy<ISubscriptionContextFacade>((m, a) => { calls++; return Task.CompletedTask; });
        var handler = new EnterpriseSubscriptionPendingInstallationEventHandler(requests, unit, facade, Profiles("Homeowner"),
            NullLogger<EnterpriseSubscriptionPendingInstallationEventHandler>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new EnterpriseSubscriptionPendingInstallationEvent(
            "sub-synthetic", "us-synthetic", "sub_synthetic", 3, 10, DateTime.UtcNow.AddDays(30), DateTime.UtcNow), default));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Expired_pending_installation_is_cancelled_with_placeholder_refund_and_unpublished_event()
    {
        var subscription = PendingInstallation(DateTime.UtcNow.AddDays(-31));
        var saves = 0;
        var repository = AssignmentHttpFixture.Proxy<ISubscriptionRepository>((m, a) =>
            m.Name == nameof(ISubscriptionRepository.FindPendingInstallationExpiredAsync) ? Task.FromResult<IEnumerable<Subscription>>([subscription]) : throw new InvalidOperationException("Unexpected subscription call"));
        var unit = AssignmentHttpFixture.Proxy<IUnitOfWork>((m, a) => { saves++; return Task.CompletedTask; });
        // Only these two services exist: resolving Planning, Assets, payment or MediatR would throw.
        var provider = new ServiceCollection().AddSingleton(repository).AddSingleton(unit).BuildServiceProvider();
        var job = new EnterpriseActivationPolicyJob(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<EnterpriseActivationPolicyJob>.Instance);
        // One isolated pass of the private check; the hosted loop is not started.
        var check = typeof(EnterpriseActivationPolicyJob).GetMethod("CheckExpiredInstallationsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)check.Invoke(job, [CancellationToken.None])!;
        Assert.True(subscription.Status.IsCancelledRefunded);
        Assert.Null(subscription.ActiveDeviceCount); Assert.Null(subscription.InstallationDeadlineAt);
        var refunded = Assert.IsType<EnterpriseSubscriptionCancelledRefundedEvent>(Assert.Single(subscription.DomainEvents));
        Assert.Equal("system", refunded.RefundId);
        Assert.Equal(1, saves);
    }
}
