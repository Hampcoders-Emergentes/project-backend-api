using Hampcoders.Electrolink.API.Profiles.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Commands;

namespace Hampcoders.Electrolink.API.Profiles.Domain.Services;

public interface IStaffMemberCommandService
{
    /// <returns>The created staff member.</returns>
    Task<StaffMember> Handle(CreateStaffMemberCommand command);
    Task Handle(AssignStaffZoneCommand command);
    Task Handle(UpdateStaffMemberDataCommand command);
    Task Handle(DeactivateStaffMemberCommand command);
    Task Handle(GrantStaffIoTCertificationCommand command);
    Task Handle(RevokeStaffIoTCertificationCommand command);
}
