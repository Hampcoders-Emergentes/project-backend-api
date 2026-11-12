using System.Net.Mime;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Planning.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Planning.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Hampcoders.Electrolink.API.Shared.Infrastructure.OpenApi;
using RequireAccessRole = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.RequireAccessRoleAttribute;

namespace Hampcoders.Electrolink.API.Planning.Interfaces.REST;

[ApiController]
[Route("api/v1/service-assignments")]
[Produces("application/json")]
public class ServiceAssignmentController : ControllerBase
{
    private readonly IServiceAssignmentCommandService _commandService;
    private readonly IServiceDesignQueryService _queryService;
    private readonly IServiceAssignmentRepository _assignmentRepository;
    private readonly ILogger<ServiceAssignmentController> _logger;
 
    public ServiceAssignmentController(
        IServiceAssignmentCommandService      commandService,
        IServiceDesignQueryService            queryService,
        IServiceAssignmentRepository          assignmentRepository,
        ILogger<ServiceAssignmentController>  logger)
    {
        _commandService = commandService;
        _queryService   = queryService;
        _assignmentRepository = assignmentRepository;
        _logger         = logger;
    }
 
    // ──────────────────────────────────────────────────────────────────────
    // QUERIES
    // ──────────────────────────────────────────────────────────────────────
 
    /// <summary>
    /// Devuelve el estado actual de la cola de matching, separada en dos niveles:
    /// solicitudes prioritarias (plan Premium) y solicitudes normales.
    ///
    /// Dentro de cada nivel el orden es FIFO (createdAt ASC), lo cual es consistente
    /// con el índice parcial (is_priority DESC, created_at ASC) definido en la BD.
    ///
    /// Uso: panel de administración y monitoreo del proceso de asignación.
    /// </summary>
    [HttpGet("queue")]
    [RequireAccessRole(AccessRole.Admin, AccessRole.SuperAdmin, AccessRole.FieldManager)]
    [ProducesResponseType(typeof(MatchingQueueResource), StatusCodes.Status200OK)]
    public async Task<ActionResult<MatchingQueueResource>> GetMatchingQueue(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _queryService.Handle(new GetMatchingQueueQuery(page, pageSize));
 
        var resource = MatchingQueueResourceFromQueryResultAssembler.ToResource(
            result.PriorityQueue.Select(q => (q.RequestId, q.HomeownerId, q.IsPriority, q.CreatedAt.DateTime)),
            result.NormalQueue.Select(q   => (q.RequestId, q.HomeownerId, q.IsPriority, q.CreatedAt.DateTime)),
            result.TotalPending);
 
        return Ok(resource);
    }
 
    // ──────────────────────────────────────────────────────────────────────
    // COMMANDS
    // ──────────────────────────────────────────────────────────────────────
 
    /// <summary>
    /// Dispara manualmente el algoritmo de matching para una solicitud específica
    /// en estado PENDING_ASSIGNMENT.
    ///
    /// Este endpoint normalmente no es necesario: el algoritmo se ejecuta de forma
    /// automática vía la policy ServiceRequestCreatedEventHandler inmediatamente
    /// después de que la solicitud se confirma.
    ///
    /// Casos de uso válidos:
    ///   - Reintento manual tras un fallo de matching con todos los candidatos agotados
    ///   - Reintento de administrador tras incorporación de nuevos técnicos al área
    ///   - Testing y debugging del algoritmo de asignación en entornos de desarrollo
    ///
    /// El algoritmo aplicará la misma lógica interna: búsqueda por geolocalización,
    /// verificación de stock, selección por rating DESC, y snapshot inmutable del recipe.
    ///
    /// Responde 200 si el matching fue exitoso (se encontró y asignó un candidato).
    /// Responde 422 si no hay candidatos disponibles (se crea un ServiceAssignment con
    /// status FAILED y retryCount incrementado).
    /// </summary>
    [HttpPost("execute")]
    [RequireAccessRole(AccessRole.Admin, AccessRole.SuperAdmin, AccessRole.FieldManager)]
    [ProducesResponseType(typeof(MatchingResultResource), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MatchingResultResource), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExecuteMatchingAlgorithm(
        [FromBody] ExecuteMatchingAlgorithmResource resource)
    {
        try
        {
            var command = ExecuteMatchingAlgorithmCommandFromResourceAssembler.ToCommand(resource);
 
            await _commandService.Handle(command);
 
            _logger.LogInformation(
                "Matching algorithm executed successfully for RequestId {RequestId}",
                resource.RequestId);
 
            var successResult = MatchingResultResourceAssembler.ToResource(
                resource.RequestId,
                assigned: true);
 
            return Ok(successResult);
        }
        catch (ServiceRequestNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidRequestStatusException ex)
        {
            // La solicitud no está en PENDING_ASSIGNMENT; no se puede ejecutar matching
            return BadRequest(new { message = ex.Message });
        }
        catch (NoCandidatesAvailableException ex)
        {
            // El algoritmo se ejecutó pero no encontró técnicos válidos.
            // Se creó un ServiceAssignment con status FAILED; el retry con backoff
            // está gestionado por el Outbox Pattern (ver sección 7 del documento).
            _logger.LogWarning(
                "Matching algorithm found no candidates for RequestId {RequestId}. Reason: {Reason}",
                resource.RequestId, ex.Message);
 
            var failedResult = MatchingResultResourceAssembler.ToResource(
                resource.RequestId,
                assigned:      false,
                failureReason: ex.Message);
 
            return UnprocessableEntity(failedResult);
        }
    }

    /// <summary>
    /// Returns detailed matching information for a specific assignment, including the AI reasoning
    /// and scoring method used. This endpoint is intended for administrative audit and tracing purposes.
    /// </summary>
    [HttpGet("{assignmentId}/matching-details")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(MatchingScoreResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MatchingScoreResource>> GetMatchingDetails(string assignmentId)
    {
        var assignment = await _assignmentRepository.FindByIdAsync(AssignmentId.From(assignmentId));

        if (assignment is null || assignment.MatchingScore is null)
            return NotFound(new { message = "Assignment not found or no matching details available." });

        var resource = MatchingScoreResourceAssembler.ToResource(assignment.MatchingScore);
        return Ok(resource);
    }
}
 
