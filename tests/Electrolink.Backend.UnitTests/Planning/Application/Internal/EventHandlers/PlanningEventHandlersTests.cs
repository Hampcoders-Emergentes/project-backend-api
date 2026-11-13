using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Monitoring.Interfaces.ACL;
using Hampcoders.Electrolink.API.Planning.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Events;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Planning.Domain.Services;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Events;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Events;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.ACL;
using Microsoft.Extensions.Logging.Abstractions;
using static Electrolink.Backend.UnitTests.Planning.PlanningTestData;

namespace Electrolink.Backend.UnitTests.Planning.Application.Internal.EventHandlers;

public class AnomalyDetectedEventHandlerTests
{
    [Fact]
    public async Task Handle_WithAnomaly_CreatesPendingSuggestionForOwner()
    {
        // Arrange
        var suggestions = new Fake<IServiceSuggestionRepository>();
        var sut = new AnomalyDetectedEventHandler(suggestions.Object, new Fake<IUnitOfWork>().Object,
            NullLogger<AnomalyDetectedEventHandler>.Instance);

        // Act
        await sut.Handle(new AnomalyDetectedEvent("an-1", "dev-1", "prop-1", Homeowner, "OVERCURRENT", "HIGH", "EDGE", false, DateTime.UtcNow),
            CancellationToken.None);

        // Assert
        var suggestion = suggestions.Calls.Single().Arg<ServiceSuggestion>();
        Assert.Equal((Homeowner, "prop-1", "an-1", ESuggestionStatus.Pending),
            (suggestion.ClientId, suggestion.PropertyId, suggestion.AnomalyId, suggestion.Status));
    }
}

public class EnterpriseSubscriptionPendingInstallationEventHandlerTests
{
    private readonly Fake<IServiceRequestRepository> _requests = new();
    private readonly Fake<ISubscriptionContextFacade> _subscriptions = new();
    private readonly Fake<IProfilesContextFacade> _profiles = CompanyProfiles();

    private EnterpriseSubscriptionPendingInstallationEventHandler CreateSut() => new(_requests.Object, new Fake<IUnitOfWork>().Object,
        _subscriptions.Object, new ExternalProfilesService(_profiles.Object), NullLogger<EnterpriseSubscriptionPendingInstallationEventHandler>.Instance);

    private static EnterpriseSubscriptionPendingInstallationEvent Event(int devices) =>
        new("sub-1", Ids.User, "stripe-1", devices, 10, DateTime.UtcNow.AddDays(10), DateTime.UtcNow);

    [Fact]
    public async Task Handle_WithoutDevices_CreatesNoRequest()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(Event(0), CancellationToken.None);

        // Assert
        Assert.Empty(_requests.Calls);
    }

    [Fact]
    public async Task Handle_WhenOwnerHasNoActiveCompany_ThrowsInvalidOperationException()
    {
        // Arrange
        _profiles.Returns(nameof(IProfilesContextFacade.GetProfileClaimsAsync), null);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Event(2), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_WithDevices_CreatesInstallationRequestAndRecordsIt()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(Event(3), CancellationToken.None);

        // Assert
        var request = _requests.Calls.Single().Arg<ServiceRequest>();
        Assert.Equal((ERequestStatus.PendingInstallation, 3, CompanyClient), (request.Status, request.RequiredDeviceCount, request.Client));
        Assert.Equal(new object?[] { "sub-1", request.RequestId.Value }, _subscriptions.Calls.Single().Args);
    }
}

public class PlanningLoggingEventHandlersTests
{
    [Fact]
    public async Task IoTCertificationGrantedHandle_WithEvent_CompletesSuccessfully()
    {
        // Arrange
        var sut = new IoTCertificationGrantedEventHandler(NullLogger<IoTCertificationGrantedEventHandler>.Instance);

        // Act
        var task = sut.Handle(new IoTCertificationGrantedEvent(Ids.Technician, DateTime.UtcNow), CancellationToken.None);

        // Assert
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task RequestCancelledHandle_WithEvent_CompletesSuccessfully()
    {
        // Arrange
        var sut = new RequestCancelledEventHandler(NullLogger<RequestCancelledEventHandler>.Instance);

        // Act
        var task = sut.Handle(new RequestCancelledEvent(RequestId.NewId(), Client, null, "ChangedMind", DateTime.UtcNow), CancellationToken.None);

        // Assert
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task RequestCreatedHandle_WithEvent_CompletesSuccessfully()
    {
        // Arrange
        var sut = new RequestCreatedEventHandler(NullLogger<RequestCreatedEventHandler>.Instance);
        var evt = new RequestCreatedEvent(RequestId.NewId(), Client, PropertyId.NewPropertyId(), RecipeId.NewId(), Technician, false, [],
            ETimePreference.Anytime, DateTime.UtcNow);

        // Act
        var task = sut.Handle(evt, CancellationToken.None);

        // Assert
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task TechnicianAssignedHandle_WithEvent_CompletesSuccessfully()
    {
        // Arrange
        var sut = new TechnicianAssignedEventHandler(NullLogger<TechnicianAssignedEventHandler>.Instance);
        var evt = new TechnicianAssignedEvent(RequestId.NewId(), Technician, Client, RecipeId.NewId(), DateOnly.FromDateTime(DateTime.Today),
            DateTime.UtcNow);

        // Act
        var task = sut.Handle(evt, CancellationToken.None);

        // Assert
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }
}

public class ServiceAssignmentFailedEventHandlerTests
{
    private readonly Fake<IServiceRequestRepository> _requests = new();

    private ServiceAssignmentFailedEventHandler CreateSut() =>
        new(_requests.Object, new Fake<IUnitOfWork>().Object, NullLogger<ServiceAssignmentFailedEventHandler>.Instance);

    [Fact]
    public async Task Handle_WhenRequestMissing_DoesNotUpdate()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(new ServiceAssignmentFailedEvent(RequestId.NewId(), "NO_CANDIDATES", 1, DateTime.UtcNow), CancellationToken.None);

        // Assert
        Assert.Equal(0, _requests.CallCount(nameof(IServiceRequestRepository.Update)));
    }

    [Fact]
    public async Task Handle_AfterMaxRetries_ExpiresRequest()
    {
        // Arrange
        var request = PendingAssignment();
        _requests.Returns(nameof(IServiceRequestRepository.FindByIdAsync), request);
        var sut = CreateSut();

        // Act
        await sut.Handle(new ServiceAssignmentFailedEvent(request.RequestId, "NO_CANDIDATES", 3, DateTime.UtcNow), CancellationToken.None);

        // Assert
        Assert.Equal(ERequestStatus.Expired, request.Status);
    }

    [Fact]
    public async Task Handle_BeforeMaxRetries_KeepsRequestPending()
    {
        // Arrange
        var request = PendingAssignment();
        _requests.Returns(nameof(IServiceRequestRepository.FindByIdAsync), request);
        var sut = CreateSut();

        // Act
        await sut.Handle(new ServiceAssignmentFailedEvent(request.RequestId, "NO_CANDIDATES", 2, DateTime.UtcNow), CancellationToken.None);

        // Assert
        Assert.Equal(ERequestStatus.PendingAssignment, request.Status);
    }
}

public class ServiceAutomaticallyAssignedEventHandlerTests
{
    private readonly Fake<IServiceDesignQueryService> _queries = new();
    private readonly Fake<IMonitoringContextFacade> _monitoring = new();

    private ServiceAutomaticallyAssignedEventHandler CreateSut() => new(_queries.Object, new Fake<IServiceRequestCommandService>().Object,
        new ExternalMonitoringService(_monitoring.Object), NullLogger<ServiceAutomaticallyAssignedEventHandler>.Instance);

    private static ServiceAutomaticallyAssignedEvent Event(RequestId requestId) => new(AssignmentId.NewAssignmentId(), requestId, Technician,
        RecipeSnapshot.FromRecipe(Recipe()), DateTime.UtcNow.AddDays(1), true, false, DateTime.UtcNow);

    [Fact]
    public async Task Handle_WhenRequestMissing_DoesNotCreateExecution()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(Event(RequestId.NewId()), CancellationToken.None);

        // Assert
        Assert.Empty(_monitoring.Calls);
    }

    [Fact]
    public async Task Handle_WithRequest_CreatesServiceExecutionForClientProperty()
    {
        // Arrange
        var request = PendingAssignment();
        _queries.On(nameof(IServiceDesignQueryService.Handle), a => a[0] is GetServiceRequestByIdQuery ? request : null);
        var evt = Event(request.RequestId);
        var sut = CreateSut();

        // Act
        await sut.Handle(evt, CancellationToken.None);

        // Assert
        var args = _monitoring.Calls.Single().Args;
        Assert.Equal((evt.AssignmentId.Value, Homeowner, request.PropertyId!.Value, true), (args[0], args[3], args[4], args[7]));
    }

    [Fact]
    public async Task Handle_WhenExecutionCreationFails_SwallowsException()
    {
        // Arrange
        var request = PendingAssignment();
        _queries.On(nameof(IServiceDesignQueryService.Handle), a => a[0] is GetServiceRequestByIdQuery ? request : null);
        _monitoring.Throws(nameof(IMonitoringContextFacade.CreateServiceExecutionAsync), new InvalidOperationException("down"));
        var sut = CreateSut();

        // Act
        var exception = await Record.ExceptionAsync(() => sut.Handle(Event(request.RequestId), CancellationToken.None));

        // Assert
        Assert.Null(exception);
    }
}

public class SubscriptionActivatedEventHandlerTests
{
    private readonly Fake<IServiceCatalogRepository> _catalogs = new();
    private readonly Fake<IProfilesContextFacade> _profiles = new();

    private SubscriptionActivatedEventHandler CreateSut() => new(_catalogs.Object, new Fake<IUnitOfWork>().Object,
        new ExternalProfilesService(_profiles.Object), NullLogger<SubscriptionActivatedEventHandler>.Instance);

    private static SubscriptionActivatedEvent Event(string role) =>
        new("sub-1", Ids.User, role, "PRO", "MONTHLY", "stripe-1", DateTime.UtcNow, DateTime.UtcNow.AddMonths(1), DateTime.UtcNow);

    [Fact]
    public async Task Handle_ForNonTechnicianRole_DoesNotCreateCatalog()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(Event("HOMEOWNER"), CancellationToken.None);

        // Assert
        Assert.Empty(_catalogs.Calls);
    }

    [Fact]
    public async Task Handle_WithoutTechnicianProfile_DoesNotCreateCatalog()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(Event("TECHNICIAN"), CancellationToken.None);

        // Assert
        Assert.Empty(_catalogs.Calls);
    }

    [Fact]
    public async Task Handle_WhenCatalogExists_DoesNotAddAnother()
    {
        // Arrange
        _profiles.Returns(nameof(IProfilesContextFacade.GetTechnicianIdByUserIdAsync), Ids.Technician);
        _catalogs.Returns(nameof(IServiceCatalogRepository.FindByTechnicianIdAsync), ServiceCatalog.Create(Technician));
        var sut = CreateSut();

        // Act
        await sut.Handle(Event("TECHNICIAN"), CancellationToken.None);

        // Assert
        Assert.Equal(0, _catalogs.CallCount(nameof(IServiceCatalogRepository.AddAsync)));
    }

    [Fact]
    public async Task Handle_ForTechnicianWithoutCatalog_CreatesCatalog()
    {
        // Arrange
        _profiles.Returns(nameof(IProfilesContextFacade.GetTechnicianIdByUserIdAsync), Ids.Technician);
        var sut = CreateSut();

        // Act
        await sut.Handle(Event("TECHNICIAN"), CancellationToken.None);

        // Assert
        Assert.Equal(Technician, _catalogs.CallsTo(nameof(IServiceCatalogRepository.AddAsync)).Single().Arg<ServiceCatalog>().TechnicianId);
    }
}
