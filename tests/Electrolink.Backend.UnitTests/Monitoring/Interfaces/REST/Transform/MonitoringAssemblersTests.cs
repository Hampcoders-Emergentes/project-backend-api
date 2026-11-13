using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Monitoring.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Monitoring.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Interfaces.REST.Resources;
using static Electrolink.Backend.UnitTests.Monitoring.MonitoringTestData;

namespace Electrolink.Backend.UnitTests.Monitoring.Interfaces.REST.Transform;

public class MonitoringCommandAssemblersTests
{
    private static readonly string Execution = ServiceExecutionId.NewId().Value;
    private static readonly DateTime At = new(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CancelServiceExecution_ToCommandFromResource_ParsesCancelledByIgnoringCase()
    {
        // Act
        var command = CancelServiceExecutionCommandFromResourceAssembler.ToCommandFromResource(Execution, Homeowner,
            new CancelServiceExecutionResource("homeowner", "Other", "note", true));

        // Assert
        Assert.Equal(new CancelServiceExecutionCommand(ServiceExecutionId.From(Execution), Homeowner, ECancelledBy.Homeowner, "Other", "note",
            true), command);
    }

    [Fact]
    public void CancelServiceExecution_ToCommandFromResourceWithUnknownActor_ThrowsArgumentException()
    {
        // Act
        var act = () => CancelServiceExecutionCommandFromResourceAssembler.ToCommandFromResource(Execution, Homeowner,
            new CancelServiceExecutionResource("Admin", "Other", null, false));

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void CompleteServiceExecution_ToCommand_UsesCurrentUtcTime()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var command = CompleteServiceExecutionCommandAssembler.ToCommand(Execution, Ids.Technician);

        // Assert
        Assert.Equal(Technician, command.TechnicianId);
        Assert.InRange(command.CompletedAt, before, DateTime.UtcNow);
    }

    [Fact]
    public void CreateServiceExecution_ToCommandFromResourceWithUnknownCurrency_ThrowsArgumentException()
    {
        // Arrange
        var resource = new CreateServiceExecutionResource(AssignmentId.NewAssignmentId().Value, "req-1", Ids.Technician, Homeowner,
            PropertyId.NewPropertyId().Value, new RecipeSnapshotResource("recipe-1", "Install", "Installation",
                [new ComponentRequirementResource("ctype-1", 1, true)], 150, "Btc", 90, 6, At), At, false);

        // Act
        var act = () => CreateServiceExecutionCommandFromResourceAssembler.ToCommandFromResource(resource);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void ExtendServiceWaitTime_ToCommandFromResource_MapsOwnerAndMinutes()
    {
        // Act
        var command = ExtendServiceWaitTimeCommandFromResourceAssembler.ToCommandFromResource(Execution, Homeowner, new ExtendWaitTimeResource(20));

        // Assert
        Assert.Equal((Owner, 20), (command.Owner, command.ExtendMinutes));
    }

    [Fact]
    public void RecordComponentsUsed_ToCommandFromResource_MapsEveryItem()
    {
        // Arrange
        var resource = new RecordComponentsUsedResource([new ComponentUsageItemResource("ctype-1", "Breaker", 2, 1)], At);

        // Act
        var command = RecordComponentsUsedCommandFromResourceAssembler.ToCommandFromResource(Execution, Ids.Technician, resource);

        // Assert
        Assert.Equal(new ComponentUsageItem("ctype-1", "Breaker", 2, 1), Assert.Single(command.ComponentsUsed));
    }

    [Fact]
    public void RegisterWorkPhoto_ToCommandFromResource_ParsesPhotoTypeIgnoringCase()
    {
        // Act
        var command = RegisterWorkPhotoCommandFromResourceAssembler.ToCommandFromResource(Execution, Ids.Technician,
            new RegisterWorkPhotoResource("prov-1", "https://img/1.jpg", null, "after", "jpg", 10, At, null));

        // Assert
        Assert.Equal(new RegisterWorkPhotoCommand(ServiceExecutionId.From(Execution), Technician, "prov-1", "https://img/1.jpg", null,
            EPhotoType.After, "jpg", 10, At, null), command);
    }

    [Fact]
    public void RegisterWorkPhoto_ToCommandFromResourceWithMalformedExecutionId_ThrowsInvalidIdException()
    {
        // Act
        var act = () => RegisterWorkPhotoCommandFromResourceAssembler.ToCommandFromResource("run-1", Ids.Technician,
            new RegisterWorkPhotoResource("prov-1", "https://img/1.jpg", null, "after", "jpg", 10, At, null));

        // Assert
        Assert.Throws<InvalidIdException>(act);
    }

    [Fact]
    public void StartServiceExecution_ToCommandFromResource_MapsStartTime()
    {
        // Act
        var command = StartServiceExecutionCommandFromResourceAssembler.ToCommandFromResource(Execution, Ids.Technician,
            new StartServiceExecutionResource(At));

        // Assert
        Assert.Equal(new StartServiceExecutionCommand(ServiceExecutionId.From(Execution), Technician, At), command);
    }

    [Fact]
    public void SubmitClientReview_ToCommandFromResource_ParsesCategories()
    {
        // Arrange
        var resource = new SubmitReviewResource(5, "great", new Dictionary<string, int> { ["workquality"] = 4 });

        // Act
        var command = SubmitClientReviewCommandFromResourceAssembler.ToCommandFromResource(Execution, Homeowner, resource);

        // Assert
        Assert.Equal(4, command.Categories[EEvaluationCategory.WorkQuality]);
    }

    [Fact]
    public void SubmitClientReview_ToCommandFromResourceWithUnknownCategory_ThrowsArgumentException()
    {
        // Arrange
        var resource = new SubmitReviewResource(5, null, new Dictionary<string, int> { ["vibes"] = 4 });

        // Act
        var act = () => SubmitClientReviewCommandFromResourceAssembler.ToCommandFromResource(Execution, Homeowner, resource);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void SubmitTechnicianReview_ToCommandFromResource_ParsesTechnicianCategories()
    {
        // Arrange
        var resource = new SubmitReviewResource(3, null, new Dictionary<string, int> { ["Payment"] = 2 });

        // Act
        var command = SubmitTechnicianReviewCommandFromResourceAssembler.ToCommandFromResource(Execution, Ids.Technician, resource);

        // Assert
        Assert.Equal((3, 2), (command.Rating, command.Categories[ETechnicianEvaluationCategory.Payment]));
    }

    [Fact]
    public void UpdateTechnicalReport_ToCommandFromResource_MapsReportFields()
    {
        // Act
        var command = UpdateTechnicalReportCommandFromResourceAssembler.ToCommandFromResource(Execution, Ids.Technician,
            new UpdateTechnicalReportResource("content", "findings", "recs", At, "iot"));

        // Assert
        Assert.Equal(new UpdateTechnicalReportCommand(ServiceExecutionId.From(Execution), Technician, "content", "findings", "recs", "iot", At),
            command);
    }

    [Fact]
    public void UploadWorkPhoto_ToCommandFromResource_ParsesPhotoType()
    {
        // Act
        var command = UploadWorkPhotoCommandFromResourceAssembler.ToCommandFromResource(Execution, Ids.Technician,
            new UploadWorkPhotoResource("BEFORE", "https://img/1.jpg", "prov-1", null, 10, "png", At, "n"));

        // Assert
        Assert.Equal(new UploadWorkPhotoCommand(ServiceExecutionId.From(Execution), Technician, EPhotoType.Before, "https://img/1.jpg",
            "prov-1", null, 10, "png", At, "n"), command);
    }

    [Fact]
    public void UploadWorkPhoto_ToCommandFromResourceWithUnknownType_ThrowsArgumentException()
    {
        // Act
        var act = () => UploadWorkPhotoCommandFromResourceAssembler.ToCommandFromResource(Execution, Ids.Technician,
            new UploadWorkPhotoResource("selfie", "https://img/1.jpg", "prov-1", null, 10, "png", At, null));

        // Assert
        Assert.Throws<ArgumentException>(act);
    }
}

public class ServiceExecutionResourceFromEntityAssemblerTests
{
    [Fact]
    public void ToResourceFromEntity_NotifiedExecution_MapsRecipeAndEmptyWorkLog()
    {
        // Arrange
        var execution = Notified();

        // Act
        var resource = ServiceExecutionResourceFromEntityAssembler.ToResourceFromEntity(execution);

        // Assert
        Assert.Equal(("Notified", "Panel install", "Installation", 150m, 90), (resource.Status, resource.ServiceName, resource.ServiceCategory,
            resource.TotalPrice, resource.EstimatedDuration));
        Assert.Equal(new WorkLogSummaryResource(0, false, false), resource.WorkLog);
    }

    [Fact]
    public void ToResourceFromEntity_ReadyExecution_ReportsCompleteWorkLog()
    {
        // Arrange
        var execution = ReadyToComplete();

        // Act
        var resource = ServiceExecutionResourceFromEntityAssembler.ToResourceFromEntity(execution);

        // Assert
        Assert.Equal(new WorkLogSummaryResource(1, true, true), resource.WorkLog);
    }

    [Fact]
    public void ToWorkLogResourceFromEntity_ReadyExecution_MapsPhotosReportAndComponents()
    {
        // Arrange
        var execution = ReadyToComplete();

        // Act
        var resource = ServiceExecutionResourceFromEntityAssembler.ToWorkLogResourceFromEntity(execution);

        // Assert
        Assert.Equal(("Before", "Replaced breaker", 1), (Assert.Single(resource.Photos).PhotoType, resource.ReportContent, resource.ReportVersion));
        Assert.Equal(new ComponentUsageResource("ctype-1", 1, 1, 0), Assert.Single(resource.Components));
    }
}
