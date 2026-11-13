using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Assets.Interfaces.ACL;
using Hampcoders.Electrolink.API.Planning.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Planning.Domain.Services;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using static Electrolink.Backend.UnitTests.Planning.PlanningTestData;

namespace Electrolink.Backend.UnitTests.Planning.Application.Internal.CommandServices;

public class MaintenanceScheduleCommandServiceTests
{
    private readonly Fake<IMaintenanceScheduleRepository> _schedules = new();
    private readonly Fake<IMediator> _mediator = new();

    private MaintenanceScheduleCommandService CreateSut() =>
        new(_schedules.Object, new Fake<IUnitOfWork>().Object, _mediator.Object, NullLogger<MaintenanceScheduleCommandService>.Instance);

    [Fact]
    public async Task Handle_Schedule_PersistsActiveScheduleAndPublishesEvent()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(new SchedulePreventiveMaintenanceCommand(Company, PropertyId.NewPropertyId().Value, MaintenanceFrequency.Monthly,
            DateTime.UtcNow.AddDays(30)));

        // Assert
        Assert.True(_schedules.Calls.Single().Arg<MaintenanceSchedule>().IsActive);
        Assert.Equal(1, _mediator.CallCount(nameof(IMediator.Publish)));
    }

    [Fact]
    public async Task Handle_CompleteVisitWhenScheduleMissing_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new CompletePreventiveVisitCommand(ScheduleId.NewId().Value, DateTime.UtcNow));

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_CompleteVisitOnQuarterlySchedule_MovesNextVisitThreeMonths()
    {
        // Arrange
        var schedule = MaintenanceSchedule.Create(Company, PropertyId.NewPropertyId().Value, MaintenanceFrequency.Quarterly, DateTime.UtcNow);
        _schedules.Returns(nameof(IMaintenanceScheduleRepository.FindByIdAsync), schedule);
        var completedAt = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var sut = CreateSut();

        // Act
        await sut.Handle(new CompletePreventiveVisitCommand(schedule.Id.Value, completedAt));

        // Assert
        Assert.Equal((completedAt, completedAt.AddMonths(3)), (schedule.LastVisitDate!.Value, schedule.NextVisitDate));
    }
}

public class ServiceSuggestionCommandServiceTests
{
    private readonly Fake<IServiceSuggestionRepository> _suggestions = new();
    private readonly Fake<IServiceRequestRepository> _requests = new();

    private ServiceSuggestionCommandService CreateSut() =>
        new(_suggestions.Object, _requests.Object, new Fake<IUnitOfWork>().Object, NullLogger<ServiceSuggestionCommandService>.Instance);

    private ServiceSuggestion GivenSuggestion(string client = Homeowner)
    {
        var suggestion = ServiceSuggestion.Create(client, null, PropertyId.NewPropertyId().Value, "an-1", "dev-1", "OVERCURRENT", "HIGH",
            DateTime.UtcNow.AddDays(2));
        _suggestions.Returns(nameof(IServiceSuggestionRepository.FindBySuggestionIdAsync), suggestion);
        return suggestion;
    }

    [Fact]
    public async Task Handle_AcceptWhenSuggestionMissing_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new AcceptServiceSuggestionCommand("missing", Client));

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_AcceptSuggestionOfAnotherClient_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var suggestion = GivenSuggestion("ho-2");
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new AcceptServiceSuggestionCommand(suggestion.SuggestionId, Client));

        // Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(act);
    }

    [Fact]
    public async Task Handle_AcceptOwnSuggestion_CreatesRequestForSuggestedProperty()
    {
        // Arrange
        var suggestion = GivenSuggestion();
        var sut = CreateSut();

        // Act
        var requestId = await sut.Handle(new AcceptServiceSuggestionCommand(suggestion.SuggestionId, Client));

        // Assert
        var request = _requests.Calls.Single().Arg<ServiceRequest>();
        Assert.Equal((requestId, suggestion.PropertyId), (request.RequestId.Value, request.PropertyId!.Value));
        Assert.Equal((ESuggestionStatus.ConvertedToRequest, requestId), (suggestion.Status, suggestion.ConvertedToRequestId));
    }

    [Fact]
    public async Task Handle_DismissOwnSuggestion_ExpiresSuggestion()
    {
        // Arrange
        var suggestion = GivenSuggestion();
        var sut = CreateSut();

        // Act
        await sut.Handle(new DismissServiceSuggestionCommand(suggestion.SuggestionId, Client));

        // Assert
        Assert.Equal(ESuggestionStatus.Expired, suggestion.Status);
    }

    [Fact]
    public async Task Handle_DismissSuggestionOfAnotherClient_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var suggestion = GivenSuggestion("ho-2");
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new DismissServiceSuggestionCommand(suggestion.SuggestionId, Client));

        // Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(act);
    }
}

public class SubscriptionInstallationPropertyServiceTests
{
    private readonly Fake<IServiceRequestRepository> _requests = new();
    private readonly Fake<IProfilesContextFacade> _profiles = CompanyProfiles();
    private readonly Fake<IAssetsContextFacade> _assets = new();
    private readonly Fake<IServiceAssignmentCommandService> _assignments = new();
    private readonly string _property = PropertyId.NewPropertyId().Value;

    public SubscriptionInstallationPropertyServiceTests() =>
        _assets.Returns(nameof(IAssetsContextFacade.GetActiveOwnedPropertyGeolocationAsync), (-12.1, -77.0));

    private SubscriptionInstallationPropertyService CreateSut() => new(_requests.Object, new ExternalProfilesService(_profiles.Object),
        new ExternalAssetsService(_assets.Object), new Fake<IUnitOfWork>().Object, _assignments.Object);

    private ServiceRequest GivenRequest(ServiceRequest request)
    {
        _requests.Returns(nameof(IServiceRequestRepository.FindByIdAsync), request);
        return request;
    }

    private AttachInstallationPropertyCommand Command(string requestId) => new(Ids.Actor, requestId, _property);

    [Fact]
    public async Task Handle_WhenActorIsNotCompany_ThrowsInstallationPropertyActorDeniedException()
    {
        // Arrange
        _profiles.Returns(nameof(IProfilesContextFacade.GetProfileClaimsAsync), Claims("Technician", Ids.Technician));
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Command("req-1"));

        // Assert
        await Assert.ThrowsAsync<InstallationPropertyActorDeniedException>(act);
    }

    [Fact]
    public async Task Handle_WithMalformedRequestId_ThrowsInstallationRequestUnavailableException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Command("order-1"));

        // Assert
        await Assert.ThrowsAsync<InstallationRequestUnavailableException>(act);
    }

    [Fact]
    public async Task Handle_WhenRequestBelongsToAnotherUser_ThrowsInstallationRequestUnavailableException()
    {
        // Arrange
        var request = GivenRequest(InstallationRequest(Ids.OtherUser));
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Command(request.RequestId.Value));

        // Assert
        await Assert.ThrowsAsync<InstallationRequestUnavailableException>(act);
    }

    [Fact]
    public async Task Handle_WhenRequestIsNotSubscriptionInstallation_ThrowsInstallationRequestUnavailableException()
    {
        // Arrange
        var request = GivenRequest(Draft(CompanyClient));
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Command(request.RequestId.Value));

        // Assert
        await Assert.ThrowsAsync<InstallationRequestUnavailableException>(act);
    }

    [Fact]
    public async Task Handle_WhenPropertyAlreadyAttached_ThrowsInstallationRequestConflictException()
    {
        // Arrange
        var request = GivenRequest(InstallationRequest());
        request.AttachInstallationProperty(PropertyId.NewPropertyId(), Location);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Command(request.RequestId.Value));

        // Assert
        await Assert.ThrowsAsync<InstallationRequestConflictException>(act);
    }

    [Fact]
    public async Task Handle_WhenPropertyIsNotOwnedAndActive_ThrowsInstallationRequestUnavailableException()
    {
        // Arrange
        var request = GivenRequest(InstallationRequest());
        _assets.Returns(nameof(IAssetsContextFacade.GetActiveOwnedPropertyGeolocationAsync), null);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Command(request.RequestId.Value));

        // Assert
        await Assert.ThrowsAsync<InstallationRequestUnavailableException>(act);
    }

    [Fact]
    public async Task Handle_WithOwnedProperty_AttachesPropertyAndRunsMatching()
    {
        // Arrange
        var request = GivenRequest(InstallationRequest());
        var sut = CreateSut();

        // Act
        var (result, outcome) = await sut.Handle(Command(request.RequestId.Value));

        // Assert
        Assert.Equal((EInstallationMatchingOutcome.Assigned, _property, EServiceCategory.Installation),
            (outcome, result.PropertyId!.Value, result.RequestedCategory!.Value));
        Assert.Equal(request.RequestId, _assignments.Calls.Single().Arg<ExecuteMatchingAlgorithmCommand>().RequestId);
    }

    [Fact]
    public async Task Handle_WhenMatchingFindsNoCandidates_ReturnsNoCandidatesOutcome()
    {
        // Arrange
        var request = GivenRequest(InstallationRequest());
        _assignments.Throws(nameof(IServiceAssignmentCommandService.Handle), new NoCandidatesAvailableException("NO_CANDIDATES_AVAILABLE"));
        var sut = CreateSut();

        // Act
        var (_, outcome) = await sut.Handle(Command(request.RequestId.Value));

        // Assert
        Assert.Equal(EInstallationMatchingOutcome.NoCandidates, outcome);
    }
}

public class ServiceAssignmentCommandServiceTests
{
    private readonly Fake<IServiceAssignmentRepository> _assignments = new();
    private readonly Fake<IServiceRequestRepository> _requests = new();
    private readonly Fake<IProfilesContextFacade> _profiles = new();
    private readonly Fake<IAssetsContextFacade> _assets = new();
    private readonly Fake<IServiceCatalogRepository> _catalogs = new();
    private readonly Fake<IMatchingService> _matching = new();

    private ServiceAssignmentCommandService CreateSut() => new(_assignments.Object, _requests.Object,
        new ExternalProfilesService(_profiles.Object), new ExternalAssetsService(_assets.Object), _catalogs.Object, new Fake<IUnitOfWork>().Object,
        new Fake<IMediator>().Object, _matching.Object, NullLogger<ServiceAssignmentCommandService>.Instance);

    private ServiceRequest GivenRequest(ServiceRequest request)
    {
        _requests.Returns(nameof(IServiceRequestRepository.FindByIdAsync), request);
        return request;
    }

    private void GivenTechnicianWithRecipeAndStock()
    {
        _profiles.Returns(nameof(IProfilesContextFacade.GetTechniciansInAreaAsync),
            new List<(string, string, string)> { (Ids.Technician, "prof-9", "Tec One") });
        _profiles.Returns(nameof(IProfilesContextFacade.GetTechnicianDetailsAsync), (Ids.Technician, -12.1, -77.0, 5, (IEnumerable<string>)["Repair"]));
        _catalogs.Returns(nameof(IServiceCatalogRepository.FindActiveRecipesByCategoryAndTechnicianIdsAsync),
            new Dictionary<TechnicianId, Hampcoders.Electrolink.API.Planning.Domain.Model.Entities.ServiceRecipe> { [Technician] = Recipe() });
        _assets.Returns(nameof(IAssetsContextFacade.CheckAllComponentsInStockAsync), true);
        _matching.On(nameof(IMatchingService.FindBestCandidateAsync), a =>
        {
            var best = ((IReadOnlyList<EnrichedCandidate>)a[1]!).First();
            return new MatchingResult(best, [], MatchingScore.Deterministic(0.8), TimeSpan.Zero);
        });
    }

    [Fact]
    public async Task Handle_AssignStaffWhenRequestMissing_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new AssignStaffToServiceCommand(RequestId.NewId(), "stf-1"));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_AssignStaffToPendingRequest_MarksRequestAssignedToStaff()
    {
        // Arrange
        var request = GivenRequest(PendingAssignment());
        var sut = CreateSut();

        // Act
        await sut.Handle(new AssignStaffToServiceCommand(request.RequestId, "stf-1"));

        // Assert
        Assert.Equal((ERequestStatus.Assigned, EAssignmentType.Staff, "stf-1"),
            (request.Status, request.AssignmentType!.Value, request.AssignedStaffMemberId!.Value));
        Assert.Equal(1, _assignments.CallCount(nameof(IServiceAssignmentRepository.AddAsync)));
    }

    [Fact]
    public async Task Handle_MatchingWhenRequestHasNoGeolocation_ThrowsInvalidOperationException()
    {
        // Arrange
        var request = GivenRequest(Draft());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new ExecuteMatchingAlgorithmCommand(request.RequestId));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_MatchingWhenRequestHasNoCategory_ThrowsInvalidOperationException()
    {
        // Arrange
        var request = GivenRequest(WithProperty());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new ExecuteMatchingAlgorithmCommand(request.RequestId));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_MatchingWithoutCandidates_RecordsFailedAssignmentAndThrows()
    {
        // Arrange
        var request = GivenRequest(PendingAssignment());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new ExecuteMatchingAlgorithmCommand(request.RequestId));

        // Assert
        await Assert.ThrowsAsync<NoCandidatesAvailableException>(act);
        Assert.Equal(1, _assignments.CallCount(nameof(IServiceAssignmentRepository.AddAsync)));
    }

    [Fact]
    public async Task Handle_MatchingWhenTechnicianLacksStock_ThrowsNoCandidatesAvailableException()
    {
        // Arrange
        var request = GivenRequest(PendingAssignment());
        GivenTechnicianWithRecipeAndStock();
        _assets.Returns(nameof(IAssetsContextFacade.CheckAllComponentsInStockAsync), false);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new ExecuteMatchingAlgorithmCommand(request.RequestId));

        // Assert
        await Assert.ThrowsAsync<NoCandidatesAvailableException>(act);
    }

    [Fact]
    public async Task Handle_MatchingWithEligibleTechnician_AssignsRequestToBestCandidate()
    {
        // Arrange
        var request = GivenRequest(PendingAssignment());
        GivenTechnicianWithRecipeAndStock();
        var sut = CreateSut();

        // Act
        await sut.Handle(new ExecuteMatchingAlgorithmCommand(request.RequestId));

        // Assert
        Assert.Equal((ERequestStatus.Assigned, Technician), (request.Status, request.SelectedTechnicianId));
        Assert.Equal(1, _assignments.CallCount(nameof(IServiceAssignmentRepository.AddAsync)));
    }
}
