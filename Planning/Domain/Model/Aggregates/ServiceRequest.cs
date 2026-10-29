using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Events;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;

public class ServiceRequest : BaseAggregateRoot
{
    public RequestId RequestId { get; private set; }
    public ClientIdentity Client { get; private set; }
    public ERequestStatus Status { get; private set; }

    public PropertyId? PropertyId { get; private set; }
    public Geolocation? Geolocation { get; private set; }
    public RecipeId? SelectedRecipeId { get; private set; }
    public EServiceCategory? RequestedCategory { get; private set; }
    public TechnicianId? SelectedTechnicianId { get; private set; }
    public StaffMemberId? AssignedStaffMemberId { get; private set; }
    public EAssignmentType? AssignmentType { get; private set; }
    public RecipeSnapshot? RecipeSnapshot { get; private set; }
    public AssignmentId? AssignmentId { get; private set; }
    public RequestPreferences? Preferences { get; private set; }
    public bool IsPriority { get; private set; }
    public bool RequiresIoTCertifiedTechnician { get; private set; }
    public IoTContextSnapshot? IotContextSnapshot { get; private set; }
    
    // ── IoT Installation fields ──────────────────────────────
    public string? SubscriptionId { get; private set; }
    public string? UserId { get; private set; }
    public int RequiredDeviceCount { get; private set; }
    public DateTime? InstallationDeadlineAt { get; private set; }

    private ServiceRequest() { }

    public HomeownerId HomeownerId => Client.IsHomeowner
        ? Client.ToHomeownerId()
        : throw new InvalidOperationException("Client is not a Homeowner");

    // ── Factory ───────────────────────────────────────────

    public static ServiceRequest Initiate(
        ClientIdentity client,
        bool canMarkAsPriority,
        int? remainingRequests)
    {
        var request = new ServiceRequest
        {
            RequestId   = RequestId.NewId(),
            Client      = client,
            Status      = ERequestStatus.Draft,
            IsPriority  = false,
        };
        request.RaiseDomainEvent(new ServiceRequestInitiatedEvent(
            request.RequestId, client, canMarkAsPriority, remainingRequests, DateTime.UtcNow));
        return request;
    }

    public static ServiceRequest CreateIoTInstallationRequest(
        string subscriptionId,
        string userId,
        ClientIdentity client,
        int requiredDeviceCount,
        DateTime installationDeadline)
    {
        ArgumentNullException.ThrowIfNull(client);
        var request = new ServiceRequest
        {
            RequestId = RequestId.NewId(),
            Client = client,
            SubscriptionId = subscriptionId,
            UserId = userId,
            RequiredDeviceCount = requiredDeviceCount,
            InstallationDeadlineAt = installationDeadline,
            Status = ERequestStatus.PendingInstallation,
        };
        return request;
    }

    // ── Commands ──────────────────────────────────────────
    public void SelectCategory(EServiceCategory category)
    {
        EnsureStatus(ERequestStatus.PropertySelected);
        RequestedCategory = category;
        Status = ERequestStatus.CategorySelected;
    }
    public void SelectProperty(PropertyId propertyId, Geolocation geolocation)
    {
        EnsureStatus(ERequestStatus.Draft);
        PropertyId  = propertyId;
        Geolocation = geolocation;
        Status = ERequestStatus.PropertySelected;
        RaiseDomainEvent(new PropertySelectedForRequestEvent(RequestId, propertyId, geolocation, DateTime.UtcNow));
    }

    // Subscription installation: the owner's property makes the request ready for the existing matching.
    public void AttachInstallationProperty(PropertyId propertyId, Geolocation geolocation)
    {
        EnsureStatus(ERequestStatus.PendingInstallation);
        if (SubscriptionId is null || PropertyId is not null)
            throw new InvalidRequestStatusException(RequestId, ERequestStatus.PendingInstallation, Status);
        PropertyId = propertyId;
        Geolocation = geolocation;
        RequestedCategory = EServiceCategory.Installation;
        Status = ERequestStatus.PendingAssignment;
    }

    public void SelectRecipe(RecipeId recipeId, RecipeSnapshot snapshot)
    {
        EnsureStatus(ERequestStatus.Draft);
        EnsurePropertySelected();
        SelectedRecipeId = recipeId;
        RecipeSnapshot = snapshot;
    }

    public void AddDetails(RequestPreferences preferences, bool isPriority)
    {
        EnsureStatus(ERequestStatus.CategorySelected);
        //EnsureRecipeSelected();
        Preferences = preferences;
        IsPriority  = isPriority;
        Status = ERequestStatus.ReadyToConfirm;
        RaiseDomainEvent(new ServiceDetailsAddedEvent(RequestId, preferences, isPriority, DateTime.UtcNow));
    }

    public void Confirm()
    {
        EnsureStatus(ERequestStatus.ReadyToConfirm);
        Status = ERequestStatus.PendingAssignment;
        RaiseDomainEvent(new ServiceRequestCreatedEvent(
            RequestId, Client, PropertyId!, SelectedRecipeId!,
            SelectedTechnicianId!, RecipeSnapshot!, IsPriority, DateTime.UtcNow));
    }

    public void Cancel(CancellationReason reason, string? notes = null)
    {
        var cancellable = new[]
        {
            ERequestStatus.Draft,
            ERequestStatus.ReadyToConfirm,
            ERequestStatus.PendingAssignment
        };

        if (!cancellable.Contains(Status))
            throw new CannotCancelAssignedRequestException(RequestId);

        var wasInQueue = Status == ERequestStatus.PendingAssignment;
        Status = ERequestStatus.Cancelled;
        RaiseDomainEvent(new ServiceRequestCancelledEvent(
            RequestId, Client, reason, wasInQueue, notes, DateTime.UtcNow));
    }

    public void MarkAsAssigned(AssignmentId assignmentId, TechnicianId technicianId, RecipeSnapshot snapshot)
    {
        EnsureStatus(ERequestStatus.PendingAssignment);
        Status = ERequestStatus.Assigned;
        SelectedTechnicianId = technicianId;
        AssignmentType = EAssignmentType.Technician;
        RecipeSnapshot = snapshot;
        SelectedRecipeId = snapshot.RecipeId;
        AssignmentId = assignmentId;
    }

    public void MarkAsAssignedToStaff(AssignmentId assignmentId, StaffMemberId staffMemberId)
    {
        EnsureStatus(ERequestStatus.PendingAssignment);
        Status = ERequestStatus.Assigned;
        AssignedStaffMemberId = staffMemberId;
        AssignmentType = EAssignmentType.Staff;
        AssignmentId = assignmentId;
    }

    public void Expire()
    {
        EnsureStatus(ERequestStatus.PendingAssignment);
        Status = ERequestStatus.Expired;
        RaiseDomainEvent(new ServiceRequestExpiredEvent(RequestId, Client, DateTime.UtcNow));
    }

    public void Reactivate()
    {
        if (Status != ERequestStatus.Assigned)
            throw new InvalidOperationException(
                $"ServiceRequest {RequestId} must be in Assigned status to reactivate. Current: {Status}.");

        SelectedTechnicianId = null;
        AssignmentId = null;
        RecipeSnapshot = null;
        SelectedRecipeId = null;

        Status = ERequestStatus.PendingAssignment;

        RaiseDomainEvent(new ServiceRequestReactivatedEvent(
            RequestId, Client, IsPriority, DateTime.UtcNow));
    }
    
    public void SetIotContextSnapshot(IoTContextSnapshot snapshot)
    {
        IotContextSnapshot = snapshot;
    }

    // ── Invariants ────────────────────────────────────────

    private void EnsureStatus(ERequestStatus expected)
    {
        if (Status != expected)
            throw new InvalidRequestStatusException(RequestId, expected, Status);
    }

    private void EnsurePropertySelected()
    {
        if (PropertyId is null)
            throw new PropertyNotSelectedOnRequestException(RequestId);
    }

    private void EnsureRecipeSelected()
    {
        if (SelectedRecipeId is null)
            throw new RecipeNotSelectedOnRequestException(RequestId);
    }
}