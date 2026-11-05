using System.ComponentModel.DataAnnotations;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Entities;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Events;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Monitoring.Domain.Model.Aggregates;

public class ServiceExecution : BaseAggregateRoot, IExecutable
{
    private static readonly HashSet<(EExecutionStatus From, string Action)> _validTransitions = new()
    {
        (EExecutionStatus.Notified, nameof(Start)),
        (EExecutionStatus.Notified, nameof(MarkEnRoute)),
        (EExecutionStatus.EnRoute, nameof(MarkArrived)),
        (EExecutionStatus.Arrived, nameof(Complete)),
        (EExecutionStatus.InProgress, nameof(Complete)),
        (EExecutionStatus.InProgress, nameof(UploadPhoto)),
        (EExecutionStatus.InProgress, nameof(RecordComponentsUsed)),
        (EExecutionStatus.InProgress, nameof(UpdateTechnicalReport)),
        (EExecutionStatus.InProgress, nameof(RemotelyToggleCircuit)),
        (EExecutionStatus.PendingReview, nameof(SubmitClientReview)),
        (EExecutionStatus.PendingReview, nameof(SubmitTechnicianReview)),
        (EExecutionStatus.Completed, nameof(SubmitClientReview)),
        (EExecutionStatus.Completed, nameof(SubmitTechnicianReview)),
        (EExecutionStatus.Completed, nameof(TryFinalizeAfterReviews)),
        (EExecutionStatus.PendingReview, nameof(TryFinalizeAfterReviews)),
        (EExecutionStatus.Notified, nameof(RecordNoShow)),
        (EExecutionStatus.Notified, nameof(ExtendWaitTime)),
    };

    private static readonly HashSet<EExecutionStatus> _cancelableStatuses = new()
    {
        EExecutionStatus.Notified, EExecutionStatus.EnRoute, EExecutionStatus.Arrived, EExecutionStatus.InProgress, EExecutionStatus.PendingReview
    };

    public ServiceExecutionId Id { get; private set; } = null!;
    public AssignmentId AssignmentId { get; private set; } = null!;
    public RequestId RequestId { get; private set; } = null!;
    public TechnicianId TechnicianId { get; private set; } = null!;
    public ClientIdentity Owner { get; private set; } = null!;

    public HomeownerId HomeownerId => Owner.IsHomeowner
        ? Owner.ToHomeownerId()
        : throw new InvalidOperationException("Owner is not a Homeowner");
    public PropertyId PropertyId { get; private set; } = null!;
    public RecipeSnapshot RecipeSnapshot { get; private set; } = null!;
    public EExecutionStatus Status { get; private set; }
    public DateTime ScheduledDateTime { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public EServiceType ServiceType { get; private set; }
    public IoTContextSnapshot? IotContext { get; private set; }
    private readonly List<WorkPhoto> _workPhotos = new();
    public IReadOnlyCollection<WorkPhoto> WorkPhotos => _workPhotos.AsReadOnly();
    private readonly List<ComponentUsageRecord> _componentSubstitutions = new();
    public IReadOnlyCollection<ComponentUsageRecord> ComponentSubstitutions => _componentSubstitutions.AsReadOnly();
    private readonly List<RelayActionRecord> _relayActionRecords = new();
    public IReadOnlyCollection<RelayActionRecord> RelayActionRecords => _relayActionRecords.AsReadOnly();
    [MaxLength(4000)]
    public string? TechnicalReportContent { get; private set; }
    [MaxLength(4000)]
    public string? TechnicalReportFindings { get; private set; }
    [MaxLength(4000)]
    public string? TechnicalReportRecommendations { get; private set; }
    [MaxLength(4000)]
    public string? TechnicalReportIotFindings { get; private set; }
    public int TechnicalReportVersion { get; private set; }
    public bool IsPriority { get; private set; }
    public bool IsStaffTechnician { get; private set; }
    private readonly List<ServiceEvaluation> _evaluations = new();
    public IReadOnlyCollection<ServiceEvaluation> Evaluations => _evaluations.AsReadOnly();
    public DateTime? NoShowDetectedAt { get; private set; }
    public DateTime? WaitExtendedUntil { get; private set; }
    public DateTime? EvaluationWindowExpiresAt { get; private set; }
    public bool EvaluationWindowExpired { get; private set; }

    protected ServiceExecution() { }

    public static ServiceExecution Create(CreateServiceExecutionCommand command)
    {
        if (command.AssignmentId == null) throw new ArgumentNullException(nameof(AssignmentId));
        if (command.RequestId == null) throw new ArgumentNullException(nameof(RequestId));
        if (command.TechnicianId == null) throw new ArgumentNullException(nameof(TechnicianId));
        if (command.Owner == null) throw new ArgumentNullException(nameof(Owner));
        if (command.PropertyId == null) throw new ArgumentNullException(nameof(PropertyId));
        if (command.RecipeSnapshot == null) throw new ArgumentNullException(nameof(RecipeSnapshot));

        var exec = new ServiceExecution
        {
            Id = ServiceExecutionId.NewId(),
            AssignmentId = command.AssignmentId,
            RequestId = command.RequestId,
            TechnicianId = command.TechnicianId,
            Owner = command.Owner,
            PropertyId = command.PropertyId,
            RecipeSnapshot = command.RecipeSnapshot,
            ScheduledDateTime = command.ScheduledDateTime,
            Status = EExecutionStatus.Notified,
            IsPriority = command.IsPriority,
            ServiceType = command.ServiceType,
            IotContext = command.IotContext,
            IsStaffTechnician = command.IsStaffTechnician,
            TechnicalReportVersion = 0
        };

        return exec;
    }

    public void Start(TechnicianId technicianId, DateTime startedAt)
    {
        EnsureTransitionValid(nameof(Start));
        EnsureTechnicianOwnership(technicianId);

        var previous = Status;
        Status = EExecutionStatus.InProgress;
        StartedAt = startedAt;

        RaiseDomainEvent(new ServiceExecutionStartedEvent(
            Id, AssignmentId,
            TechnicianId, Owner,
            previous, Status,
            startedAt, DateTime.UtcNow));
    }

    public void MarkEnRoute(TechnicianId technicianId, DateTime timestamp)
    {
        EnsureTransitionValid(nameof(MarkEnRoute));
        EnsureTechnicianOwnership(technicianId);

        var previous = Status;
        Status = EExecutionStatus.EnRoute;

        RaiseDomainEvent(new ServiceStatusChangedEvent(
            Id, AssignmentId, TechnicianId, Owner,
            previous, Status, timestamp, DateTime.UtcNow));
    }

    public void MarkArrived(TechnicianId technicianId, DateTime timestamp)
    {
        EnsureTransitionValid(nameof(MarkArrived));
        EnsureTechnicianOwnership(technicianId);

        var previous = Status;
        Status = EExecutionStatus.Arrived;

        RaiseDomainEvent(new ServiceStatusChangedEvent(
            Id, AssignmentId, TechnicianId, Owner,
            previous, Status, timestamp, DateTime.UtcNow));
    }

    public void UploadPhoto(
        EPhotoType photoType,
        string photoUrl,
        string providerId,
        string? thumbnailUrl,
        long sizeBytes,
        string format,
        DateTime takenAt,
        string? notes)
    {
        EnsureTransitionValid(nameof(UploadPhoto));

        if (string.IsNullOrWhiteSpace(photoUrl))
            throw new ArgumentException("Photo URL cannot be empty.", nameof(photoUrl));

        const int maxTotalPhotos = 10;
        if (_workPhotos.Count >= maxTotalPhotos)
            throw new MaxWorkPhotosExceededException(maxTotalPhotos, _workPhotos.Count);

        int maxPerType = photoType switch
        {
            EPhotoType.Before => 3,
            EPhotoType.During => 3,
            EPhotoType.After => 4,
            _ => throw new ArgumentException($"Invalid photo type: {photoType}")
        };

        int currentCountForType = _workPhotos.Count(p => p.PhotoType == photoType);
        if (currentCountForType >= maxPerType)
            throw new MaxWorkPhotosPerTypeExceededException(photoType, maxPerType, currentCountForType);

        var photo = WorkPhoto.Create(Id, photoType, photoUrl, providerId, thumbnailUrl, sizeBytes, format, takenAt, notes);
        _workPhotos.Add(photo);

        RaiseDomainEvent(new WorkPhotoUploadedEvent(
            Id, photo.Id, photoType, DateTime.UtcNow));
    }

    public void RecordComponentsUsed(IReadOnlyList<ComponentUsageRecord> components, DateTime recordedAt)
    {
        EnsureTransitionValid(nameof(RecordComponentsUsed));

        if (components is null || components.Count == 0)
            throw new ArgumentException("At least one component must be recorded.");

        _componentSubstitutions.Clear();
        foreach (var c in components)
            _componentSubstitutions.Add(c);

        bool hasOverage = _componentSubstitutions.Any(c => c.Delta > 0);

        RaiseDomainEvent(new ComponentsActuallyUsedRecordedEvent(
            Id, AssignmentId,
            _componentSubstitutions.Select(c => new ComponentUsage(
                c.ComponentTypeId, c.ComponentTypeName,
                c.QuantityUsed, c.QuantityReserved, c.Delta)).ToList(),
            hasOverage,
            recordedAt, DateTime.UtcNow));
    }

    public void UpdateTechnicalReport(string content, string findings, string recommendations, string? iotFindings, DateTime updatedAt)
    {
        EnsureTransitionValid(nameof(UpdateTechnicalReport));

        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Report content cannot be empty.", nameof(content));

        TechnicalReportContent         = content;
        TechnicalReportFindings        = findings;
        TechnicalReportRecommendations = recommendations;
        TechnicalReportIotFindings     = iotFindings;
        TechnicalReportVersion++;

        RaiseDomainEvent(new TechnicalReportUpdatedEvent(
            Id, TechnicalReportVersion, updatedAt));
    }

    public void Complete(TechnicianId technicianId, DateTime completedAt)
    {
        EnsureTransitionValid(nameof(Complete));
        EnsureTechnicianOwnership(technicianId);

        if (_workPhotos.Count == 0)
            throw new InvalidOperationException("At least one work photo is required to complete the service.");

        if (string.IsNullOrWhiteSpace(TechnicalReportContent))
            throw new InvalidOperationException("A technical report is required to complete the service.");

        if (_componentSubstitutions.Count == 0)
            throw new InvalidOperationException("At least one component record is required to complete the service.");

        if (IsStaffTechnician)
            Status = EExecutionStatus.Completed;
        else
            Status = EExecutionStatus.PendingReview;

        CompletedAt = completedAt;

        bool hasOverage = _componentSubstitutions.Any(c => c.Delta > 0);

        RaiseDomainEvent(new ServiceCompletedEvent(
            Id,
            AssignmentId,
            RequestId,
            TechnicianId,
            Owner,
            PropertyId,
            RecipeSnapshot.ServiceCategory.ToString(),
            _componentSubstitutions.Select(c => new ComponentUsage(
                c.ComponentTypeId, c.ComponentTypeName,
                c.QuantityUsed, c.QuantityReserved, c.Delta)).ToList(),
            hasOverage,
            _workPhotos.Select(p => p.PhotoUrl).ToList(),
            TechnicalReportContent,
            completedAt, DateTime.UtcNow,
            IsStaffTechnician));
    }

    public void Cancel(CancellationRequestId? cancellationRequestId, string cancelledById, ECancelledBy cancelledBy, ECancellationReason reason, string? notes, bool requestReassignment)
    {
        if (!_cancelableStatuses.Contains(Status))
            throw new InvalidOperationException($"Cannot cancel a service in status {Status}.");

        bool effectiveReassignment = cancelledBy == ECancelledBy.Technician ? false : requestReassignment;

        var previous = Status;
        Status = EExecutionStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;

        RaiseDomainEvent(new ServiceExecutionCancelledEvent(
            Id,
            AssignmentId,
            RequestId,
            TechnicianId,
            Owner,
            cancelledBy,
            reason.ToString(),
            notes,
            previous.ToString(),
            effectiveReassignment,
            CancelledAt.Value,
            DateTime.UtcNow));
    }

    public void TryFinalizeAfterReviews()
    {
        EnsureTransitionValid(nameof(TryFinalizeAfterReviews));

        bool clientEvaluated = _evaluations.Any(e => e.ReviewerRole == "Client");
        bool technicianEvaluated = _evaluations.Any(e => e.ReviewerRole == "Technician");

        if (IsStaffTechnician)
        {
            if (clientEvaluated)
                Status = EExecutionStatus.Completed;
        }
        else
        {
            if (clientEvaluated && technicianEvaluated)
                Status = EExecutionStatus.Completed;
        }
    }

    public void OpenEvaluationWindow()
    {
        if (Status != EExecutionStatus.PendingReview && Status != EExecutionStatus.Completed)
            throw new InvalidOperationException("Evaluation window can only be opened for completed or pending-review services.");

        EvaluationWindowExpiresAt = DateTime.UtcNow.AddHours(72);
        EvaluationWindowExpired = false;

        RaiseDomainEvent(new EvaluationWindowOpenedEvent(
            Id,
            AssignmentId,
            TechnicianId,
            Owner,
            EvaluationWindowExpiresAt.Value,
            DateTime.UtcNow,
            DateTime.UtcNow));
    }

    public void SubmitClientReview(ClientIdentity owner, int rating, string? comment, Dictionary<string, int> categories, DateTime submittedAt)
    {
        if (Status != EExecutionStatus.PendingReview && Status != EExecutionStatus.Completed)
            throw new InvalidOperationException(
                $"Client review can only be submitted for PendingReview or Completed services, but status is {Status}.");

        EnsureClientOwnership(owner);
        EnsureEvaluationWindowOpen();

        if (_evaluations.Any(e => e.ReviewerId == owner.ClientId && e.ReviewerRole == "Client"))
            throw new InvalidOperationException("Client has already submitted a review for this service.");

        if (rating < 1 || rating > 5)
            throw new ArgumentOutOfRangeException(nameof(rating), "Rating must be between 1 and 5.");

        var eval = ServiceEvaluation.Create(Id, owner.ClientId, TechnicianId.Value, "Client", rating, comment, categories, submittedAt);
        _evaluations.Add(eval);

        RaiseDomainEvent(new ClientReviewSubmittedEvent(
            Id, AssignmentId,
            owner.ClientId, TechnicianId.Value,
            rating, categories, submittedAt, DateTime.UtcNow));
    }

    public void SubmitTechnicianReview(TechnicianId technicianId, int rating, string? comment, Dictionary<string, int> categories, DateTime submittedAt)
    {
        if (IsStaffTechnician)
            throw new InvalidOperationException("Staff technicians cannot submit reviews for their own service.");

        if (Status != EExecutionStatus.PendingReview && Status != EExecutionStatus.Completed)
            throw new InvalidOperationException(
                $"Technician review can only be submitted for PendingReview or Completed services, but status is {Status}.");

        EnsureTechnicianOwnership(technicianId);
        EnsureEvaluationWindowOpen();

        if (_evaluations.Any(e => e.ReviewerId == technicianId.Value && e.ReviewerRole == "Technician"))
            throw new InvalidOperationException("Technician has already submitted a review for this service.");

        if (rating < 1 || rating > 5)
            throw new ArgumentOutOfRangeException(nameof(rating), "Rating must be between 1 and 5.");

        var eval = ServiceEvaluation.Create(Id, technicianId.Value, Owner.ClientId, "Technician", rating, comment, categories, submittedAt);
        _evaluations.Add(eval);

        RaiseDomainEvent(new TechnicianReviewSubmittedEvent(
            Id, AssignmentId,
            technicianId.Value, Owner.ClientId,
            rating, categories, submittedAt, DateTime.UtcNow));
    }

    public void ExpireEvaluationWindow()
    {
        if (EvaluationWindowExpired)
            return;

        EvaluationWindowExpired = true;

        bool clientEvaluated = _evaluations.Any(e => e.ReviewerRole == "Client");
        bool technicianEvaluated = _evaluations.Any(e => e.ReviewerRole == "Technician");

        if (clientEvaluated && technicianEvaluated)
            Status = EExecutionStatus.Completed;

        RaiseDomainEvent(new EvaluationWindowExpiredEvent(
            Id, AssignmentId,
            clientEvaluated, technicianEvaluated,
            DateTime.UtcNow, DateTime.UtcNow));
    }

    public void RecordNoShow(DateTime detectedAt, int minutesLate)
    {
        EnsureTransitionValid(nameof(RecordNoShow));

        NoShowDetectedAt = detectedAt;

        RaiseDomainEvent(new TechnicianNoShowDetectedEvent(
            Id, AssignmentId,
            TechnicianId, Owner,
            ScheduledDateTime, detectedAt, minutesLate, DateTime.UtcNow));
    }

    public void ExtendWaitTime(ClientIdentity owner, int extendMinutes, DateTime requestedAt)
    {
        EnsureTransitionValid(nameof(ExtendWaitTime));
        EnsureClientOwnership(owner);

        if (extendMinutes <= 0)
            throw new ArgumentException("Extension minutes must be positive.", nameof(extendMinutes));

        WaitExtendedUntil = (NoShowDetectedAt ?? requestedAt).AddMinutes(extendMinutes);

        RaiseDomainEvent(new ServiceWaitTimeExtendedEvent(
            Id, owner,
            extendMinutes, WaitExtendedUntil.Value, requestedAt, DateTime.UtcNow));
    }

    public void RemotelyToggleCircuit(TechnicianId technicianId, ERelayState targetState, string actorId)
    {
        EnsureTransitionValid(nameof(RemotelyToggleCircuit));
        EnsureTechnicianOwnership(technicianId);

        if (IotContext is null)
            throw new InvalidOperationException("No IoT device is associated with this service execution.");

        var record = RelayActionRecord.Create(Id, IotContext.DeviceId, DateTime.UtcNow);
        _relayActionRecords.Add(record);

        RaiseDomainEvent(new CircuitToggleRequestedEvent(
            Id,
            IotContext.DeviceId,
            targetState,
            DateTime.UtcNow));
    }

    public void RecordCircuitToggle(DeviceId deviceId, ERelayState targetState, ERelayActionStatus actionStatus, string? failureReason)
    {
        if (IotContext is null)
            throw new InvalidOperationException("No IoT device context found for this service execution.");

        if (IotContext.DeviceId != deviceId)
            throw new InvalidOperationException($"Device {deviceId} is not associated with this service execution.");

        var pending = _relayActionRecords.FirstOrDefault(r =>
            r.DeviceId == deviceId && r.Status == ERelayActionStatus.Pending);
        if (pending is null)
            throw new InvalidOperationException("No pending relay action found for this service execution.");

        if (actionStatus == ERelayActionStatus.Executed)
            pending.MarkExecuted(DateTime.UtcNow);
        else
            pending.MarkFailed(failureReason ?? "Unknown error", DateTime.UtcNow);

        RaiseDomainEvent(new CircuitToggleRecordedEvent(
            Id, deviceId, targetState,
            actionStatus, failureReason, DateTime.UtcNow));
    }

    private void EnsureTransitionValid(string action)
    {
        if (!_validTransitions.Contains((Status, action)))
            throw new InvalidOperationException(
                $"Transition from {Status} via {action} is not valid.");
    }

    public void EnsureTechnicianOwnership(TechnicianId technicianId)
    {
        if (TechnicianId != technicianId)
            throw new UnauthorizedAccessException(
                $"Technician {technicianId} is not assigned to this service execution.");
    }

    private void EnsureClientOwnership(ClientIdentity owner)
    {
        if (Owner != owner)
            throw new UnauthorizedAccessException(
                $"Client {owner} is not the owner of this service execution.");
    }

    private void EnsureEvaluationWindowOpen()
    {
        if (EvaluationWindowExpired || EvaluationWindowExpiresAt is null)
            throw new InvalidOperationException("The evaluation window has expired or has not been opened.");

        if (DateTime.UtcNow > EvaluationWindowExpiresAt.Value)
            throw new InvalidOperationException("The evaluation window has expired.");
    }
}
