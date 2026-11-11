using Hampcoders.Electrolink.API.Processing.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Processing.Domain.Services;
using Hampcoders.Electrolink.API.Processing.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Shared.Interfaces.REST;
using Microsoft.AspNetCore.Mvc;
using Hampcoders.Electrolink.API.Shared.Infrastructure.OpenApi;

namespace Hampcoders.Electrolink.API.Processing.Interfaces.REST;

[ApiController]
[Route("api/v1/iot/devices/{deviceId}/relay")]
[Produces("application/json")]
public class RelayControlController(IRelayCommandService relayService) : ControllerBase
{
    [HttpPost]
    [ApiDocOperation(Summary = "Issue relay command (technician)", OperationId = "IssueRelayCommand")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> IssueRelayCommand(
        string deviceId,
        [FromBody] IssueRelayCommandResource resource)
    {
        try
        {
            // The JWT subject is a user id; relay authorization is keyed by the technician's role subject.
            var technicianId = User.GetTechnicianId();

            var command = new IssueRelayCommandCommand(
                deviceId, resource.PropertyId, technicianId,
                resource.ServiceRequestId, resource.TargetRelayState);

            await relayService.Handle(command);
            return NoContent();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex)   { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex)        { return NotFound(new { message = ex.Message }); }
    }
}
