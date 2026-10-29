using Hampcoders.Electrolink.API.Assets.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Events;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Planning.Infrastructure.Persistence.EFC.Repositories;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Repository;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using IAssetsContextFacade = Hampcoders.Electrolink.API.Assets.Interfaces.ACL.IAssetsContextFacade;
using SubscriptionBusinessRole =Hampcoders.Electrolink.API.Subscriptions.Domain.Model.ValueObjects.BusinessRole;
namespace Electrolink.Backend.Characterization.Tests.Assets;

// M3.17: the assigned technician selects devices only through the protected HTTP assignment; no event assigns stock.
public sealed class SubscriptionInstallationProvisioningTests
{
    static readonly Type[] DeviceWriters = [typeof(IIoTDeviceCommandService), typeof(DeviceAssignmentService)];

    static IEnumerable<Type> NotificationHandlers() => typeof(IoTDevice).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && t.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>)));

    [Fact]
    public void Device_writing_event_handlers_are_the_reviewed_set_without_scheduling_assignment()
    {
        var writers = NotificationHandlers()
            .Where(t => t.GetConstructors().Any(c => c.GetParameters().Any(p => DeviceWriters.Contains(p.ParameterType))))
            .Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal);
        // Connection status and completed-service installation; a new device-writing handler needs its own review.
        Assert.Equal(new[] { "DeviceDisconnectedEventHandler", "DeviceReconnectedEventHandler", "ServiceCompletedIoTInstallationEventHandler" }, writers);
    }

    [Fact]
    public void No_in_process_event_carries_a_device_selection()
    {
        var handled = NotificationHandlers().SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>))
            .Select(i => i.GetGenericArguments()[0]).Distinct();
        Assert.DoesNotContain(handled, e => e.GetProperty("SelectedDeviceId") is not null);
    }

    [Fact]
    public void Implicit_first_stock_assignment_surface_is_absent()
    {
        Assert.DoesNotContain(typeof(IAssetsContextFacade).GetMethods(), m => m.Name.StartsWith("Assign", StringComparison.Ordinal));
        Assert.Null(typeof(IIoTDeviceRepository).GetMethod("FindFirstInStockAsync"));
    }

    [Fact]
    public void Subscription_installation_request_has_no_property_client_or_technician_to_assign()
    {
        var request = ServiceRequest.CreateIoTInstallationRequest("sub-synthetic", "us-synthetic", 3, DateTime.UtcNow.AddDays(30));
        Assert.Equal(ERequestStatus.PendingInstallation, request.Status);
        Assert.Equal(3, request.RequiredDeviceCount);
        Assert.Null(request.PropertyId); Assert.Null(request.Client); Assert.Null(request.RequestedCategory);
        Assert.Null(request.SelectedTechnicianId); Assert.Null(request.AssignmentId);
        // Matching assigns only PendingAssignment requests; snapshot is never reached past the status check.
        Assert.Throws<InvalidRequestStatusException>(() =>
            request.MarkAsAssigned(AssignmentId.NewAssignmentId(), TechnicianId.From("tech-synthetic"), null!));
    }

    [Fact]
    public async Task Subscription_installation_request_is_not_an_assigned_installation_for_http_assign()
    {
        var request = ServiceRequest.CreateIoTInstallationRequest("sub-synthetic", "us-synthetic", 1, DateTime.UtcNow.AddDays(30));
        var requests = AssignmentHttpFixture.Proxy<IServiceRequestRepository>((m, a) =>
            m.Name == nameof(IServiceRequestRepository.FindByIdAsync) ? Task.FromResult<ServiceRequest?>(request) : throw new InvalidOperationException("Unexpected request call"));
        // The persisted status is rejected before the assignment table is read, so no context is needed.
        var reader = new InstallationAssignmentReader(requests, null!);
        Assert.False(await reader.IsAssignedInstallation(request.RequestId.Value, "prop-synthetic", "tech-synthetic", ClientIdentity.FromCompany("comp-synthetic")));
    }

    [Fact]
    public async Task First_installed_device_activates_subscription_with_one_device_although_more_were_required()
    {
        var subscription = Subscription.Initialize(UserId.From("us-synthetic"), ProfileId.From("prof-synthetic"),
            SubscriptionBusinessRole.Company, StripeCustomerId.From("cus_synthetic"), new EnterpriseBillingPolicy());
        subscription.ActivateEnterprisePendingInstallation(StripeSubscriptionId.From("sub_synthetic"),
            BillingPeriod.Of(DateTime.UtcNow, DateTime.UtcNow.AddMonths(1)), 3, 10, PlanType.EnterpriseBasic);
        subscription.SetInstallationServiceRequestId("req-synthetic");
        subscription.ClearDomainEvents();
        var saves = 0;
        var repository = AssignmentHttpFixture.Proxy<ISubscriptionRepository>((m, a) => m.Name switch
        {
            nameof(ISubscriptionRepository.FindByPendingInstallationServiceRequestIdAsync) => Task.FromResult<Subscription?>((string)a![0]! == "req-synthetic" ? subscription : null),
            nameof(ISubscriptionRepository.Update) => null,
            _ => throw new InvalidOperationException("Unexpected subscription call")
        });
        var unit = AssignmentHttpFixture.Proxy<IUnitOfWork>((m, a) => { saves++; return Task.CompletedTask; });
        var mediator = AssignmentHttpFixture.Proxy<IMediator>((m, a) => Task.CompletedTask);
        var handler = new DeviceInstalledEventHandler(repository, unit, mediator, NullLogger<DeviceInstalledEventHandler>.Instance);
        await handler.Handle(new DeviceInstalledEvent("dev-synthetic", "SYNTHETIC-1", "prop-synthetic", "tech-synthetic", "req-synthetic", "synthetic-1", DateTime.UtcNow), default);
        // Characterization of the open cardinality decision; not an approved rule for RequiredDeviceCount > 1.
        Assert.True(subscription.Status.Value == ESubscriptionStatus.Active);
        Assert.Equal(1, subscription.ActiveDeviceCount);
        Assert.Equal(1, saves);
    }
}
