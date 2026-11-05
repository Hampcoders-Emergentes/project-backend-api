using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Monitoring.Domain.Services;
using Hampcoders.Electrolink.API.Monitoring.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Monitoring.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Infrastructure;
using Hampcoders.Electrolink.API.Shared.Interfaces.REST;
using Microsoft.AspNetCore.Mvc;

namespace Hampcoders.Electrolink.API.Monitoring.Interfaces.REST.Controllers;

[ApiController]
[Route("api/v1/service-executions/{executionId}/actions")]
[Produces("application/json")]
public class ServiceExecutionActionsController(
    IServiceExecutionCommandService commandService,
    IServiceExecutionQueryService queryService,
    IProfilesContextFacade profiles) : ControllerBase
{
    [HttpPost("start")]
    [ProducesResponseType(typeof(ServiceExecutionResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceExecutionResource>> Start(
        [FromBody] StartServiceExecutionResource resource)
    {
        try
        {
            var executionId = RouteData.Values["executionId"]?.ToString()
                ?? throw new ArgumentException("executionId is required in the route.");
            var technicianId = User.GetTechnicianId();
            var command = StartServiceExecutionCommandFromResourceAssembler.ToCommandFromResource(executionId, technicianId, resource);
            var execution = await commandService.Handle(command);
            return Ok(ServiceExecutionResourceFromEntityAssembler.ToResourceFromEntity(execution));
        }
        catch (KeyNotFoundException ex)      { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (UnauthorizedAccessException)  { return Forbid(); }
    }

    [HttpPost("photos/upload-url")]
    [ProducesResponseType(typeof(WorkPhotoUploadUrlResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkPhotoUploadUrlResource>> GetWorkPhotoUploadUrl(
        [FromQuery] string type)
    {
        try
        {
            var executionId = RouteData.Values["executionId"]?.ToString()
                ?? throw new ArgumentException("executionId is required in the route.");
            var technicianId = User.GetTechnicianId();
            var photoType = Enum.Parse<EPhotoType>(type, true);
            var command = new GetWorkPhotoUploadUrlCommand(
                ServiceExecutionId.From(executionId),
                TechnicianId.From(technicianId),
                photoType);

            var signedData = await commandService.Handle(command);
            return Ok(new WorkPhotoUploadUrlResource(
                signedData.Url,
                signedData.Signature,
                signedData.Timestamp,
                signedData.ApiKey));
        }
        catch (KeyNotFoundException ex)      { return NotFound(new { message = ex.Message }); }
        catch (ArgumentException ex)         { return BadRequest(new { message = ex.Message }); }
        catch (UnauthorizedAccessException)  { return Forbid(); }
    }

    [HttpPost("photos")]
    [ProducesResponseType(typeof(ServiceExecutionResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceExecutionResource>> RegisterWorkPhoto(
        [FromBody] RegisterWorkPhotoResource resource)
    {
        try
        {
            var executionId = RouteData.Values["executionId"]?.ToString()
                ?? throw new ArgumentException("executionId is required in the route.");
            var technicianId = User.GetTechnicianId();
            var command = RegisterWorkPhotoCommandFromResourceAssembler.ToCommandFromResource(
                executionId, technicianId, resource);

            var execution = await commandService.Handle(command);
            return Ok(ServiceExecutionResourceFromEntityAssembler.ToResourceFromEntity(execution));
        }
        catch (KeyNotFoundException ex)              { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex)         { return BadRequest(new { message = ex.Message }); }
        catch (MaxWorkPhotosExceededException ex)    { return BadRequest(new { message = ex.Message }); }
        catch (MaxWorkPhotosPerTypeExceededException ex) { return BadRequest(new { message = ex.Message }); }
        catch (UnauthorizedAccessException)          { return Forbid(); }
    }

    [HttpPost("components")]
    [ProducesResponseType(typeof(ServiceExecutionResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ServiceExecutionResource>> RecordComponents(
        [FromBody] RecordComponentsUsedResource resource)
    {
        try
        {
            var executionId = RouteData.Values["executionId"]?.ToString()
                ?? throw new ArgumentException("executionId is required in the route.");
            var technicianId = User.GetTechnicianId();
            var command = RecordComponentsUsedCommandFromResourceAssembler.ToCommandFromResource(executionId, technicianId, resource);
            var execution = await commandService.Handle(command);
            return Ok(ServiceExecutionResourceFromEntityAssembler.ToResourceFromEntity(execution));
        }
        catch (KeyNotFoundException ex)      { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (UnauthorizedAccessException)  { return Forbid(); }
    }

    [HttpPut("report")]
    [ProducesResponseType(typeof(ServiceExecutionResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ServiceExecutionResource>> UpdateReport(
        [FromBody] UpdateTechnicalReportResource resource)
    {
        try
        {
            var executionId = RouteData.Values["executionId"]?.ToString()
                ?? throw new ArgumentException("executionId is required in the route.");
            var technicianId = User.GetTechnicianId();
            var command = UpdateTechnicalReportCommandFromResourceAssembler.ToCommandFromResource(
                executionId, technicianId, resource);

            var execution = await commandService.Handle(command);
            return Ok(ServiceExecutionResourceFromEntityAssembler.ToResourceFromEntity(execution));
        }
        catch (KeyNotFoundException ex)      { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (UnauthorizedAccessException)  { return Forbid(); }
    }

    [HttpPost("complete")]
    [ProducesResponseType(typeof(ServiceExecutionResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ServiceExecutionResource>> Complete()
    {
        try
        {
            var executionId = RouteData.Values["executionId"]?.ToString()
                ?? throw new ArgumentException("executionId is required in the route.");
            var technicianId = User.GetTechnicianId();
            var command = CompleteServiceExecutionCommandAssembler.ToCommand(executionId, technicianId);
            var execution = await commandService.Handle(command);
            return Ok(ServiceExecutionResourceFromEntityAssembler.ToResourceFromEntity(execution));
        }
        catch (KeyNotFoundException ex)      { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return UnprocessableEntity(new { message = ex.Message }); }
        catch (UnauthorizedAccessException)  { return Forbid(); }
    }

    [HttpPost("cancel")]
    [ProducesResponseType(typeof(ServiceExecutionResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ServiceExecutionResource>> Cancel(
        [FromBody] CancelServiceExecutionResource resource)
    {
        try
        {
            var executionId = RouteData.Values["executionId"]?.ToString()
                ?? throw new ArgumentException("executionId is required in the route.");
            var actorId = User.GetUserId();
            var command = CancelServiceExecutionCommandFromResourceAssembler.ToCommandFromResource(
                executionId, actorId, resource);

            // CancelledBy comes from the client, so it must match the actor's actual role in this execution.
            var (subject, denied) = await this.ResolveSubjectAsync(profiles);
            if (denied is not null) return denied;
            var current = await queryService.Handle(new GetServiceExecutionByIdQuery(ServiceExecutionId.From(executionId)));
            var isTechnician = current is not null && subject!.IsTechnician(current.TechnicianId);
            var isClient = current is not null && subject!.IsClient(current.Owner);
            if (!isTechnician && !isClient) return this.ResourceNotFound();
            if (command.CancelledBy != (isTechnician ? ECancelledBy.Technician : ECancelledBy.Homeowner)) return Forbid();

            var execution = await commandService.Handle(command);
            return Ok(ServiceExecutionResourceFromEntityAssembler.ToResourceFromEntity(execution));
        }
        catch (KeyNotFoundException ex)      { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("extend-wait")]
    [ProducesResponseType(typeof(ServiceExecutionResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ServiceExecutionResource>> ExtendWait(
        [FromBody] ExtendWaitTimeResource resource)
    {
        try
        {
            var executionId = RouteData.Values["executionId"]?.ToString()
                ?? throw new ArgumentException("executionId is required in the route.");
            var ownerId = User.GetRoleSubjectId();
            var command = ExtendServiceWaitTimeCommandFromResourceAssembler.ToCommandFromResource(
                executionId, ownerId, resource);

            var execution = await commandService.Handle(command);
            return Ok(ServiceExecutionResourceFromEntityAssembler.ToResourceFromEntity(execution));
        }
        catch (KeyNotFoundException ex)      { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (UnauthorizedAccessException)  { return Forbid(); }
    }

    [HttpPost("reviews/client")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitClientReview(
        [FromBody] SubmitReviewResource resource)
    {
        try
        {
            var executionId = RouteData.Values["executionId"]?.ToString()
                ?? throw new ArgumentException("executionId is required in the route.");
            var ownerId = User.GetRoleSubjectId();
            var command = SubmitClientReviewCommandFromResourceAssembler.ToCommandFromResource(
                executionId, ownerId, resource);

            await commandService.Handle(command);
            return NoContent();
        }
        catch (InvalidOperationException ex)  { return BadRequest(new { message = ex.Message }); }
        catch (ArgumentOutOfRangeException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex)        { return NotFound(new { message = ex.Message }); }
        catch (UnauthorizedAccessException)    { return Forbid(); }
    }

    [HttpPost("reviews/technician")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitTechnicianReview(
        [FromBody] SubmitReviewResource resource)
    {
        try
        {
            var executionId = RouteData.Values["executionId"]?.ToString()
                ?? throw new ArgumentException("executionId is required in the route.");
            var technicianId = User.GetTechnicianId();
            var command = SubmitTechnicianReviewCommandFromResourceAssembler.ToCommandFromResource(
                executionId, technicianId, resource);

            await commandService.Handle(command);
            return NoContent();
        }
        catch (InvalidOperationException ex)  { return BadRequest(new { message = ex.Message }); }
        catch (ArgumentOutOfRangeException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex)        { return NotFound(new { message = ex.Message }); }
        catch (UnauthorizedAccessException)    { return Forbid(); }
    }
}
