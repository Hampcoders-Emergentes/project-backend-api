using Hampcoders.Electrolink.API.Monitoring.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Entities;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Monitoring.Domain.Repositories;
using Hampcoders.Electrolink.API.Monitoring.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Entities;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Infrastructure;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces;
using MediatR;

namespace Hampcoders.Electrolink.API.Monitoring.Application.Internal.CommandServices;

public class ServiceExecutionCommandService(
    IServiceExecutionRepository repository,
    IFileStorageService fileStorageService,
    IOrphanedFileRepository orphanedFileRepository,
    IExternalIoTService externalIoTService,
    IUnitOfWork unitOfWork,
    IMediator mediator,
    ILogger<ServiceExecutionCommandService> logger)
    : IServiceExecutionCommandService
{

    public async Task<ServiceExecution> Handle(CreateServiceExecutionCommand command)
    {
        logger.LogInformation("[Monitoring BC] Creating ServiceExecution for assignment {AssignmentId}", command.AssignmentId);

        var existing = await repository.FindByAssignmentIdAsync(command.AssignmentId);
        if (existing is not null)
            throw new InvalidOperationException(
                $"A ServiceExecution already exists for assignment {command.AssignmentId}.");

        var execution = ServiceExecution.Create(command);

        await repository.AddAsync(execution);
        await unitOfWork.CompleteAsync();
        await PublishAndClearEventsAsync(execution);

        return execution;
    }

    public async Task<ServiceExecution> Handle(StartServiceExecutionCommand command)
    {
        var execution = await GetOrThrowAsync(command.ExecutionId);

        execution.Start(command.TechnicianId, command.StartedAt);

        await unitOfWork.CompleteAsync();
        await PublishAndClearEventsAsync(execution);

        return execution;
    }

    public async Task<ServiceExecution> Handle(UploadWorkPhotoCommand command)
    {
        var execution = await GetOrThrowAsync(command.ExecutionId);
        execution.EnsureTechnicianOwnership(command.TechnicianId);

        if (!Enum.TryParse<EPhotoType>(command.PhotoType.ToString(), true, out var photoType))
            throw new ArgumentException($"Invalid photo type: {command.PhotoType}");

        execution.UploadPhoto(
            photoType,
            command.PhotoUrl,
            command.ProviderId,
            command.ThumbnailUrl,
            command.SizeBytes,
            command.Format,
            command.TakenAt,
            command.Notes);

        await unitOfWork.CompleteAsync();
        await PublishAndClearEventsAsync(execution);

        return execution;
    }

    public async Task<ServiceExecution> Handle(RegisterWorkPhotoCommand command)
    {
        var execution = await GetOrThrowAsync(command.ExecutionId);
        execution.EnsureTechnicianOwnership(command.TechnicianId);

        try
        {
            execution.UploadPhoto(
                command.PhotoType,
                command.PublicUrl,
                command.ProviderId,
                command.ThumbnailUrl,
                command.SizeBytes,
                command.Format,
                command.TakenAt,
                command.Notes);

            await unitOfWork.CompleteAsync();
            await PublishAndClearEventsAsync(execution);

            return execution;
        }
        catch
        {
            var folder = $"electrolink/operation/{execution.Id.Value}/{command.PhotoType}";
            await orphanedFileRepository.AddAsync(
                OrphanedFileDeletion.Create(command.ProviderId, folder, "Work photo registration failed"));
            await unitOfWork.CompleteAsync();
            throw;
        }
    }

    public async Task<SignedUploadData> Handle(GetWorkPhotoUploadUrlCommand command)
    {
        var execution = await GetOrThrowAsync(command.ExecutionId);
        execution.EnsureTechnicianOwnership(command.TechnicianId);

        return await fileStorageService.GetSignedUploadUrlForWorkPhotoAsync(execution.Id.Value, command.PhotoType.ToString());
    }

    public async Task<ServiceExecution> Handle(RecordComponentsUsedCommand command)
    {
        var execution = await GetOrThrowAsync(command.ExecutionId);
        execution.EnsureTechnicianOwnership(command.TechnicianId);

        var records = command.ComponentsUsed.Select(dto =>
            ComponentUsageRecord.Create(
                ComponentUsageId.NewId(),
                execution.Id,
                dto.ComponentTypeId,
                dto.ComponentTypeName,
                dto.QuantityUsed,
                dto.QuantityReserved)).ToList();

        execution.RecordComponentsUsed(records, command.RecordedAt);

        await unitOfWork.CompleteAsync();
        await PublishAndClearEventsAsync(execution);

        return execution;
    }

    public async Task<ServiceExecution> Handle(UpdateTechnicalReportCommand command)
    {
        var execution = await GetOrThrowAsync(command.ExecutionId);
        execution.EnsureTechnicianOwnership(command.TechnicianId);

        execution.UpdateTechnicalReport(
            command.ReportContent, command.Findings,
            command.Recommendations, command.IotFindings, command.UpdatedAt);

        await unitOfWork.CompleteAsync();
        await PublishAndClearEventsAsync(execution);

        return execution;
    }

    public async Task<ServiceExecution> Handle(CompleteServiceExecutionCommand command)
    {
        var execution = await GetOrThrowAsync(command.ExecutionId);

        execution.Complete(command.TechnicianId, command.CompletedAt);

        await unitOfWork.CompleteAsync();
        await PublishAndClearEventsAsync(execution);

        logger.LogInformation(
            "[Monitoring BC] Service {AssignmentId} completed by technician {TechnicianId}",
            execution.AssignmentId.Value, command.TechnicianId);

        return execution;
    }

    public async Task<ServiceExecution> Handle(CancelServiceExecutionCommand command)
    {
        var execution = await GetOrThrowAsync(command.ExecutionId);

        if (!Enum.TryParse<ECancelledBy>(command.CancelledBy.ToString(), true, out var cancelledBy))
            throw new ArgumentException($"Invalid cancelledBy value: {command.CancelledBy}");

        if (!Enum.TryParse<ECancellationReason>(command.Reason, true, out var reason))
            throw new ArgumentException($"Invalid cancellation reason: {command.Reason}");

        execution.Cancel(
            command.CancellationRequestId,
            command.ActorId, cancelledBy, reason, command.Notes, command.RequestReassignment);

        await unitOfWork.CompleteAsync();
        await PublishAndClearEventsAsync(execution);

        return execution;
    }

    public async Task<ServiceExecution> Handle(ExtendServiceWaitTimeCommand command)
    {
        var execution = await GetOrThrowAsync(command.ExecutionId);

        execution.ExtendWaitTime(command.Owner, command.ExtendMinutes, command.RequestedAt);

        await unitOfWork.CompleteAsync();
        await PublishAndClearEventsAsync(execution);

        return execution;
    }

    public async Task<ServiceExecution> Handle(SubmitClientReviewCommand command)
    {
        var execution = await GetOrThrowAsync(command.ExecutionId);

        execution.SubmitClientReview(
            command.ReviewerId, command.Rating,
            command.Comment, command.Categories.ToDictionary(k => k.Key.ToString(), v => v.Value), command.SubmittedAt);

        await unitOfWork.CompleteAsync();
        await PublishAndClearEventsAsync(execution);

        return execution;
    }

    public async Task<ServiceExecution> Handle(RemotelyToggleCircuitCommand command)
    {
        var execution = await GetOrThrowAsync(command.ExecutionId);

        execution.RemotelyToggleCircuit(command.TechnicianId, command.TargetState, command.ActorId);

        await unitOfWork.CompleteAsync();
        await PublishAndClearEventsAsync(execution);

        if (execution.IotContext is not null)
        {
            await externalIoTService.IssueRelayCommandAsync(
                execution.Id.Value,
                execution.IotContext.DeviceId.Value,
                command.TargetState.ToString(),
                command.ActorId);
        }

        return execution;
    }

    public async Task<ServiceExecution> Handle(RecordCircuitToggleCommand command)
    {
        var execution = await GetOrThrowAsync(command.ExecutionId);

        execution.RecordCircuitToggle(command.DeviceId, command.TargetState, command.ActionStatus, command.FailureReason);

        await unitOfWork.CompleteAsync();
        await PublishAndClearEventsAsync(execution);

        return execution;
    }

    public async Task<ServiceExecution> Handle(SubmitTechnicianReviewCommand command)
    {
        var execution = await GetOrThrowAsync(command.ExecutionId);

        execution.SubmitTechnicianReview(
            command.ReviewerId, command.Rating,
            command.Comment, command.Categories.ToDictionary(k => k.Key.ToString(), v => v.Value), command.SubmittedAt);

        await unitOfWork.CompleteAsync();
        await PublishAndClearEventsAsync(execution);

        return execution;
    }

    private async Task<ServiceExecution> GetOrThrowAsync(ServiceExecutionId executionId)
    {
        var execution = await repository.FindByIdAsync(executionId);
        return execution
            ?? throw new KeyNotFoundException($"ServiceExecution {executionId} not found.");
    }

    private async Task PublishAndClearEventsAsync(ServiceExecution execution)
    {
        var events = execution.DomainEvents.ToArray();
        execution.ClearDomainEvents();
        foreach (var domainEvent in events)
            await mediator.Publish(domainEvent, CancellationToken.None);
    }
}
