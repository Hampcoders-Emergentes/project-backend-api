using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Monitoring.Domain.Services;
using Hampcoders.Electrolink.API.Monitoring.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Monitoring.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Interfaces.REST;
using Microsoft.AspNetCore.Mvc;

namespace Hampcoders.Electrolink.API.Monitoring.Interfaces.REST;

[ApiController]
[Route("api/v1/service-executions")]
[Produces("application/json")]
public class ServiceExecutionsController(
    IServiceExecutionCommandService commandService,
    IServiceExecutionQueryService queryService,
    IProfilesContextFacade profiles) : ControllerBase
{
    // Visible only to its assigned technician and its client.
    private async Task<(ServiceExecution? Execution, ActionResult? Denied)> FindVisibleExecutionAsync(string executionId)
    {
        var (subject, denied) = await this.ResolveSubjectAsync(profiles);
        if (denied is not null) return (null, denied);

        var execution = await queryService.Handle(new GetServiceExecutionByIdQuery(ServiceExecutionId.From(executionId)));
        return execution is not null && (subject!.IsTechnician(execution.TechnicianId) || subject.IsClient(execution.Owner))
            ? (execution, null)
            : (null, this.ResourceNotFound());
    }

    private async Task<(TechnicianId? Technician, ActionResult? Denied)> AuthorizeTechnicianRouteAsync(string technicianId)
    {
        var (subject, denied) = await this.ResolveSubjectAsync(profiles);
        if (denied is not null) return (null, denied);
        if (subject!.Technician is null) return (null, StatusCode(StatusCodes.Status403Forbidden));
        if (technicianId != "me" && !subject.IsTechnician(technicianId)) return (null, this.ResourceNotFound());
        return (subject.Technician, null);
    }

    [HttpPost]
    [RequireAccessRole(AccessRole.Admin, AccessRole.SuperAdmin, AccessRole.FieldManager)]
    [ProducesResponseType(typeof(ServiceExecutionResource), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ServiceExecutionResource>> CreateServiceExecution(
        [FromBody] CreateServiceExecutionResource resource)
    {
        try
        {
            var command = CreateServiceExecutionCommandFromResourceAssembler.ToCommandFromResource(resource);
            var execution = await commandService.Handle(command);
            var result = ServiceExecutionResourceFromEntityAssembler.ToResourceFromEntity(execution);

            return CreatedAtAction(nameof(GetServiceExecutionById), new { executionId = result.ExecutionId }, result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpGet("{executionId}", Name = nameof(GetServiceExecutionById))]
    [ProducesResponseType(typeof(ServiceExecutionResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceExecutionResource>> GetServiceExecutionById(string executionId)
    {
        var (execution, denied) = await FindVisibleExecutionAsync(executionId);
        if (denied is not null) return denied;
        return Ok(ServiceExecutionResourceFromEntityAssembler.ToResourceFromEntity(execution!));
    }

    [HttpGet("{executionId}/work-log")]
    [ProducesResponseType(typeof(WorkLogResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkLogResource>> GetWorkLog(string executionId)
    {
        var (execution, denied) = await FindVisibleExecutionAsync(executionId);
        if (denied is not null) return denied;
        return Ok(ServiceExecutionResourceFromEntityAssembler.ToWorkLogResourceFromEntity(execution!));
    }

    [HttpGet("technicians/{technicianId}/assigned")]
    [ProducesResponseType(typeof(IEnumerable<ServiceExecutionResource>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ServiceExecutionResource>>> GetAssignedByTechnician(string technicianId)
    {
        var (technician, denied) = await AuthorizeTechnicianRouteAsync(technicianId);
        if (denied is not null) return denied;
        var executions = await queryService.Handle(new GetAssignedServicesByTechnicianQuery(technician!));
        return Ok(executions.Select(ServiceExecutionResourceFromEntityAssembler.ToResourceFromEntity));
    }

    [HttpGet("homeowners/{homeownerId}/history")]
    [ProducesResponseType(typeof(IEnumerable<ServiceExecutionResource>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ServiceExecutionResource>>> GetHistoryByHomeowner(string homeownerId)
    {
        var denied = await this.DenyUnlessClientAsync(profiles, homeownerId);
        if (denied is not null) return denied;
        var executions = await queryService.Handle(new GetServiceHistoryByClientQuery(ClientIdentity.FromOwnerId(homeownerId)));
        return Ok(executions.Select(ServiceExecutionResourceFromEntityAssembler.ToResourceFromEntity));
    }

    [HttpGet("technicians/{technicianId}/history")]
    [ProducesResponseType(typeof(IEnumerable<ServiceExecutionResource>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ServiceExecutionResource>>> GetHistoryByTechnician(string technicianId)
    {
        var (technician, denied) = await AuthorizeTechnicianRouteAsync(technicianId);
        if (denied is not null) return denied;
        var executions = await queryService.Handle(new GetServiceHistoryByTechnicianQuery(technician!));
        return Ok(executions.Select(ServiceExecutionResourceFromEntityAssembler.ToResourceFromEntity));
    }

    [HttpGet("homeowners/{homeownerId}/active")]
    [ProducesResponseType(typeof(ServiceExecutionResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceExecutionResource>> GetActiveByHomeowner(string homeownerId)
    {
        var denied = await this.DenyUnlessClientAsync(profiles, homeownerId);
        if (denied is not null) return denied;
        var execution = await queryService.Handle(new GetActiveServiceByClientQuery(ClientIdentity.FromOwnerId(homeownerId)));
        if (execution is null) return NotFound(new { message = "No active service found for this homeowner." });
        return Ok(ServiceExecutionResourceFromEntityAssembler.ToResourceFromEntity(execution));
    }
}
