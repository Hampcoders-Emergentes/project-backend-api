using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Monitoring.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Monitoring.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Events;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Monitoring.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Entities;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Infrastructure;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using static Electrolink.Backend.UnitTests.Monitoring.MonitoringTestData;

namespace Electrolink.Backend.UnitTests.Monitoring.Application.Internal.CommandServices;

public class ServiceExecutionCommandServiceTests
{
    private static readonly TechnicianId OtherTechnician = TechnicianId.From("tech-other");
    private readonly Fake<IServiceExecutionRepository> _repository = new();
    private readonly Fake<IFileStorageService> _storage = new();
    private readonly Fake<IOrphanedFileRepository> _orphans = new();
    private readonly Fake<IExternalIoTService> _iot = new();
    private readonly Fake<IUnitOfWork> _unitOfWork = new();
    private readonly Fake<IMediator> _mediator = new();

    private ServiceExecutionCommandService CreateSut() => new(_repository.Object, _storage.Object, _orphans.Object, _iot.Object,
        _unitOfWork.Object, _mediator.Object, NullLogger<ServiceExecutionCommandService>.Instance);

    private ServiceExecution Given(ServiceExecution execution)
    {
        _repository.Returns(nameof(IServiceExecutionRepository.FindByIdAsync), execution);
        return execution;
    }

    private static UploadWorkPhotoCommand Upload(ServiceExecution e, TechnicianId? technician = null, string url = "https://img/2.jpg") =>
        new(e.Id, technician ?? Technician, EPhotoType.During, url, "prov-2", null, 200, "jpg", DateTime.UtcNow, null);

    private static RegisterWorkPhotoCommand Register(ServiceExecution e) =>
        new(e.Id, Technician, "prov-3", "https://img/3.jpg", null, EPhotoType.After, "jpg", 300, DateTime.UtcNow, null);

    [Fact]
    public async Task Handle_CreateWhenAssignmentAlreadyHasExecution_ThrowsInvalidOperationException()
    {
        // Arrange
        _repository.Returns(nameof(IServiceExecutionRepository.FindByAssignmentIdAsync), Notified());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(CreateCommand());

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_CreateForNewAssignment_PersistsNotifiedExecution()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(CreateCommand());

        // Assert
        Assert.Equal(EExecutionStatus.Notified, result.Status);
        Assert.Same(result, _repository.CallsTo(nameof(IServiceExecutionRepository.AddAsync)).Single().Arg<ServiceExecution>());
    }

    [Fact]
    public async Task Handle_StartWhenExecutionMissing_ThrowsKeyNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new StartServiceExecutionCommand(ServiceExecutionId.NewId(), Technician, DateTime.UtcNow));

        // Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_StartByAssignedTechnician_MovesToInProgressAndPublishesEvent()
    {
        // Arrange
        var execution = Given(Notified());
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new StartServiceExecutionCommand(execution.Id, Technician, DateTime.UtcNow));

        // Assert
        Assert.Equal(EExecutionStatus.InProgress, result.Status);
        Assert.IsType<ServiceExecutionStartedEvent>(_mediator.Calls.Single().Args[0]);
    }

    [Fact]
    public async Task Handle_StartByAnotherTechnician_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var execution = Given(Notified());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new StartServiceExecutionCommand(execution.Id, OtherTechnician, DateTime.UtcNow));

        // Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(act);
    }

    [Fact]
    public async Task Handle_UploadPhotoByAnotherTechnician_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var execution = Given(InProgress());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Upload(execution, OtherTechnician));

        // Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(act);
    }

    [Fact]
    public async Task Handle_UploadPhotoInProgress_AddsPhoto()
    {
        // Arrange
        var execution = Given(InProgress());
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(Upload(execution));

        // Assert
        Assert.Equal("https://img/2.jpg", Assert.Single(result.WorkPhotos).PhotoUrl);
    }

    [Fact]
    public async Task Handle_UploadPhotoBeyondPerTypeLimit_ThrowsMaxWorkPhotosPerTypeExceededException()
    {
        // Arrange
        var execution = Given(InProgress());
        var sut = CreateSut();
        for (var i = 0; i < 3; i++) await sut.Handle(Upload(execution));

        // Act
        var act = () => sut.Handle(Upload(execution));

        // Assert
        await Assert.ThrowsAsync<MaxWorkPhotosPerTypeExceededException>(act);
    }

    [Fact]
    public async Task Handle_RegisterPhotoInProgress_AddsProviderPhoto()
    {
        // Arrange
        var execution = Given(InProgress());
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(Register(execution));

        // Assert
        Assert.Equal(EPhotoType.After, Assert.Single(result.WorkPhotos).PhotoType);
    }

    [Fact]
    public async Task Handle_RegisterPhotoWhenNotInProgress_SchedulesOrphanCleanupAndRethrows()
    {
        // Arrange
        var execution = Given(Notified());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Register(execution));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        var orphan = _orphans.Calls.Single().Arg<OrphanedFileDeletion>();
        Assert.Equal(("prov-3", $"electrolink/operation/{execution.Id.Value}/After"), (orphan.ProviderId, orphan.Folder));
    }

    [Fact]
    public async Task Handle_GetUploadUrlForAssignedTechnician_ReturnsSignedUpload()
    {
        // Arrange
        var execution = Given(InProgress());
        var signed = new SignedUploadData("https://upload", "sig", 1, "key");
        _storage.Returns(nameof(IFileStorageService.GetSignedUploadUrlForWorkPhotoAsync), signed);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetWorkPhotoUploadUrlCommand(execution.Id, Technician, EPhotoType.Before));

        // Assert
        Assert.Same(signed, result);
        Assert.Equal(new object?[] { execution.Id.Value, "Before" }, _storage.Calls.Single().Args);
    }

    [Fact]
    public async Task Handle_RecordComponentsUsed_ReplacesComponentRecords()
    {
        // Arrange
        var execution = Given(InProgress());
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new RecordComponentsUsedCommand(execution.Id, Technician,
            [new ComponentUsageItem("ctype-1", "Breaker", 3, 2)], DateTime.UtcNow));

        // Assert
        var record = Assert.Single(result.ComponentSubstitutions);
        Assert.Equal((3, 2, 1), (record.QuantityUsed, record.QuantityReserved, record.Delta));
    }

    [Fact]
    public async Task Handle_RecordComponentsUsedWithEmptyList_ThrowsArgumentException()
    {
        // Arrange
        var execution = Given(InProgress());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new RecordComponentsUsedCommand(execution.Id, Technician, [], DateTime.UtcNow));

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_UpdateTechnicalReport_IncrementsReportVersion()
    {
        // Arrange
        var execution = Given(InProgress());
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new UpdateTechnicalReportCommand(execution.Id, Technician, "content", "findings", "recs", null,
            DateTime.UtcNow));

        // Assert
        Assert.Equal(("content", 1), (result.TechnicalReportContent, result.TechnicalReportVersion));
    }

    [Fact]
    public async Task Handle_CompleteWithoutWorkLog_ThrowsInvalidOperationException()
    {
        // Arrange
        var execution = Given(InProgress());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new CompleteServiceExecutionCommand(execution.Id, Technician, DateTime.UtcNow));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_CompleteByPlatformTechnician_MovesToPendingReview()
    {
        // Arrange
        var execution = Given(ReadyToComplete());
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new CompleteServiceExecutionCommand(execution.Id, Technician, DateTime.UtcNow));

        // Assert
        Assert.Equal(EExecutionStatus.PendingReview, result.Status);
        Assert.IsType<ServiceCompletedEvent>(_mediator.Calls.Single().Args[0]);
    }

    [Fact]
    public async Task Handle_CompleteByStaffTechnician_MovesToCompleted()
    {
        // Arrange
        var execution = Given(ReadyToComplete(staff: true));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new CompleteServiceExecutionCommand(execution.Id, Technician, DateTime.UtcNow));

        // Assert
        Assert.Equal(EExecutionStatus.Completed, result.Status);
    }

    [Fact]
    public async Task Handle_CancelWithUnknownReason_ThrowsArgumentException()
    {
        // Arrange
        var execution = Given(Notified());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new CancelServiceExecutionCommand(execution.Id, Homeowner, ECancelledBy.Homeowner, "Aliens", null, false));

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_CancelNotifiedExecution_MarksCancelled()
    {
        // Arrange
        var execution = Given(Notified());
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new CancelServiceExecutionCommand(execution.Id, Homeowner, ECancelledBy.Homeowner, "scheduleconflict",
            null, true));

        // Assert
        Assert.Equal(EExecutionStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task Handle_CancelByTechnician_NeverRequestsReassignment()
    {
        // Arrange
        var execution = Given(Notified());
        var sut = CreateSut();

        // Act
        await sut.Handle(new CancelServiceExecutionCommand(execution.Id, Ids.Technician, ECancelledBy.Technician, "PersonalReasons", null,
            true));

        // Assert
        Assert.False(_mediator.Calls.Single().Arg<ServiceExecutionCancelledEvent>().RequestReassignment);
    }

    [Fact]
    public async Task Handle_ExtendWaitTimeByOwner_SetsExtendedDeadline()
    {
        // Arrange
        var execution = Given(Notified());
        var requestedAt = DateTime.UtcNow;
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new ExtendServiceWaitTimeCommand(execution.Id, Owner, 15, requestedAt));

        // Assert
        Assert.Equal(requestedAt.AddMinutes(15), result.WaitExtendedUntil);
    }

    [Fact]
    public async Task Handle_ExtendWaitTimeByAnotherOwner_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var execution = Given(Notified());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new ExtendServiceWaitTimeCommand(execution.Id, ClientIdentity.FromHomeowner("ho-2"), 15, DateTime.UtcNow));

        // Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(act);
    }

    [Fact]
    public async Task Handle_SubmitClientReviewOnPendingReview_AddsClientEvaluation()
    {
        // Arrange
        var execution = Given(PendingReview());
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new SubmitClientReviewCommand(execution.Id, Owner, 5, "great",
            new Dictionary<EEvaluationCategory, int> { [EEvaluationCategory.WorkQuality] = 5 }, DateTime.UtcNow));

        // Assert
        Assert.Equal(("Client", 5), (Assert.Single(result.Evaluations).ReviewerRole, result.Evaluations.Single().Rating));
    }

    [Fact]
    public async Task Handle_SubmitClientReviewBeforeCompletion_ThrowsInvalidOperationException()
    {
        // Arrange
        var execution = Given(InProgress());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new SubmitClientReviewCommand(execution.Id, Owner, 5, null, [], DateTime.UtcNow));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_SubmitTechnicianReviewWithOutOfRangeRating_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var execution = Given(PendingReview());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new SubmitTechnicianReviewCommand(execution.Id, Technician, 6, null, [], DateTime.UtcNow));

        // Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(act);
    }

    [Fact]
    public async Task Handle_SubmitTechnicianReviewOnPendingReview_AddsTechnicianEvaluation()
    {
        // Arrange
        var execution = Given(PendingReview());
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new SubmitTechnicianReviewCommand(execution.Id, Technician, 4, null,
            new Dictionary<ETechnicianEvaluationCategory, int> { [ETechnicianEvaluationCategory.Payment] = 4 }, DateTime.UtcNow));

        // Assert
        Assert.Equal("Technician", Assert.Single(result.Evaluations).ReviewerRole);
    }

    [Fact]
    public async Task Handle_RemotelyToggleCircuitWithDevice_IssuesRelayCommand()
    {
        // Arrange
        var execution = Given(InProgress(Iot()));
        var sut = CreateSut();

        // Act
        await sut.Handle(new RemotelyToggleCircuitCommand(execution.Id, Technician, ERelayState.Off, Ids.User));

        // Assert
        Assert.Equal(new object?[] { execution.Id.Value, "dev-1", "Off", Ids.User }, _iot.Calls.Single().Args);
    }

    [Fact]
    public async Task Handle_RemotelyToggleCircuitWithoutDevice_ThrowsInvalidOperationException()
    {
        // Arrange
        var execution = Given(InProgress());
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new RemotelyToggleCircuitCommand(execution.Id, Technician, ERelayState.Off, Ids.User));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Empty(_iot.Calls);
    }

    [Fact]
    public async Task Handle_RecordCircuitToggleExecuted_MarksPendingActionExecuted()
    {
        // Arrange
        var execution = Given(InProgress(Iot()));
        var sut = CreateSut();
        await sut.Handle(new RemotelyToggleCircuitCommand(execution.Id, Technician, ERelayState.Off, Ids.User));

        // Act
        var result = await sut.Handle(new RecordCircuitToggleCommand(execution.Id, DeviceId.From("dev-1"), ERelayState.Off,
            ERelayActionStatus.Executed, null));

        // Assert
        Assert.Equal(ERelayActionStatus.Executed, Assert.Single(result.RelayActionRecords).Status);
    }

    [Fact]
    public async Task Handle_RecordCircuitToggleForOtherDevice_ThrowsInvalidOperationException()
    {
        // Arrange
        var execution = Given(InProgress(Iot()));
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new RecordCircuitToggleCommand(execution.Id, DeviceId.From("dev-2"), ERelayState.Off,
            ERelayActionStatus.Executed, null));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }
}
