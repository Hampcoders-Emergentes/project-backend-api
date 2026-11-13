using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Entities;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using IoTContextSnapshot = Hampcoders.Electrolink.API.Monitoring.Domain.Model.ValueObjects.IoTContextSnapshot;
using RequestId = Hampcoders.Electrolink.API.Monitoring.Domain.Model.ValueObjects.RequestId;

namespace Electrolink.Backend.UnitTests.Monitoring;

internal static class MonitoringTestData
{
    public const string Homeowner = "ho-1";
    public static readonly TechnicianId Technician = TechnicianId.From(Ids.Technician);
    public static readonly ClientIdentity Owner = ClientIdentity.FromHomeowner(Homeowner);

    public static RecipeSnapshot Recipe() => RecipeSnapshot.Create(RecipeId.NewId(), "Panel install", EServiceCategory.Installation,
        [ComponentRequirementItem.Create("ctype-1", "Breaker", 1, true)],
        ServicePricing.Create(Money.Of(50, ECurrency.Pen), Money.Of(100, ECurrency.Pen), Money.Of(150, ECurrency.Pen)),
        EstimatedDuration.FromMinutes(90), WarrantyPeriod.OfMonths(6), DateTime.UtcNow);

    public static CreateServiceExecutionCommand CreateCommand(IoTContextSnapshot? iot = null, bool staff = false,
        DateTime? scheduledAt = null) =>
        new(AssignmentId.NewAssignmentId(), RequestId.From("req-1"), Technician, Owner, PropertyId.NewPropertyId(), Recipe(),
            scheduledAt ?? DateTime.UtcNow.AddHours(1), false, iot, IsStaffTechnician: staff);

    public static IoTContextSnapshot Iot(string device = "dev-1") => new(DeviceId.From(device), ERelayState.On, DateTime.UtcNow);

    public static ServiceExecution Notified(IoTContextSnapshot? iot = null, bool staff = false, DateTime? scheduledAt = null)
    {
        var execution = ServiceExecution.Create(CreateCommand(iot, staff, scheduledAt));
        execution.ClearDomainEvents();
        return execution;
    }

    public static ServiceExecution InProgress(IoTContextSnapshot? iot = null, bool staff = false)
    {
        var execution = Notified(iot, staff);
        execution.Start(Technician, DateTime.UtcNow);
        execution.ClearDomainEvents();
        return execution;
    }

    /// <summary>In-progress execution with photo, report and components: ready to complete.</summary>
    public static ServiceExecution ReadyToComplete(bool staff = false)
    {
        var execution = InProgress(staff: staff);
        execution.UploadPhoto(EPhotoType.Before, "https://img/1.jpg", "prov-1", null, 100, "jpg", DateTime.UtcNow, null);
        execution.UpdateTechnicalReport("Replaced breaker", "Worn breaker", "Inspect yearly", null, DateTime.UtcNow);
        execution.RecordComponentsUsed([ComponentUsageRecord.Create(ComponentUsageId.NewId(), execution.Id, "ctype-1", "Breaker", 1, 1)],
            DateTime.UtcNow);
        execution.ClearDomainEvents();
        return execution;
    }

    public static ServiceExecution PendingReview()
    {
        var execution = ReadyToComplete();
        execution.Complete(Technician, DateTime.UtcNow);
        execution.OpenEvaluationWindow();
        execution.ClearDomainEvents();
        return execution;
    }
}
