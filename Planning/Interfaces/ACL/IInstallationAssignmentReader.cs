using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
namespace Hampcoders.Electrolink.API.Planning.Interfaces.ACL;

// Read-only eligibility for a persisted installation request and its assigned technician.
public interface IInstallationAssignmentReader
{
    Task<bool> IsAssignedInstallation(string requestId, string propertyId, string technicianId, ClientIdentity propertyOwner);
}
