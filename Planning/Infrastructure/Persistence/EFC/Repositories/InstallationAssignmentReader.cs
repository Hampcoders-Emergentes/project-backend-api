using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Planning.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;
namespace Hampcoders.Electrolink.API.Planning.Infrastructure.Persistence.EFC.Repositories;

public sealed class InstallationAssignmentReader(IServiceRequestRepository requests, AppDbContext context) : IInstallationAssignmentReader
{
    public async Task<bool> IsAssignedInstallation(string requestId, string propertyId, string technicianId, ClientIdentity propertyOwner)
    {
        if (string.IsNullOrWhiteSpace(requestId) || !requestId.StartsWith("req-", StringComparison.Ordinal)) return false;
        var request = await requests.FindByIdAsync(RequestId.From(requestId));
        if (request == null || request.Status != ERequestStatus.Assigned || request.RequestedCategory != EServiceCategory.Installation
            || request.RecipeSnapshot?.ServiceCategory != EServiceCategory.Installation || request.Client != propertyOwner
            || request.PropertyId?.Value != propertyId || request.SelectedTechnicianId?.Value != technicianId || request.AssignmentId == null) return false;
        // Authorization needs only persisted relation/status columns, not matching algorithm JSON payloads.
        var assignment = await context.ServiceAssignments.AsNoTracking().Where(a => a.AssignmentId == request.AssignmentId)
            .Select(a => new { a.RequestId, a.Status, a.TechnicianId }).SingleOrDefaultAsync();
        return assignment != null && assignment.Status == EAssignmentStatus.Assigned && assignment.RequestId == request.RequestId
            && assignment.TechnicianId?.Value == technicianId;
    }
}
