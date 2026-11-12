using Hampcoders.Electrolink.API.Profiles.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Profiles.Domain.Repositories;
using Hampcoders.Electrolink.API.Profiles.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using MediatR;

namespace Hampcoders.Electrolink.API.Profiles.Application.Internal.CommandServices;

public class StaffMemberCommandService(
    IStaffMemberRepository staffMemberRepository,
    ExternalIamService externalIamService,
    IUnitOfWork unitOfWork,
    IMediator mediator,
    ILogger<StaffMemberCommandService> logger)
    : IStaffMemberCommandService
{
    public async Task<StaffMember> Handle(CreateStaffMemberCommand command)
    {
        var userExists = await externalIamService.UserExistsAsync(command.UserId);
        if (!userExists)
            throw new ArgumentException($"User {command.UserId} does not exist in IAM.");

        var existing = await staffMemberRepository.FindByUserIdAsync(command.UserId);
        if (existing is not null)
            throw new InvalidOperationException($"Staff member already exists for user {command.UserId}.");

        var staffMember = StaffMember.Create(
            command.UserId, command.FirstName, command.LastName, command.PhoneNumber);

        await staffMemberRepository.AddAsync(staffMember);
        await unitOfWork.CompleteAsync();

        foreach (var domainEvent in staffMember.DomainEvents)
            await mediator.Publish(domainEvent, CancellationToken.None);
        staffMember.ClearDomainEvents();

        logger.LogInformation("[Profiles] Staff member created: {FirstName} {LastName} (User: {UserId})",
            command.FirstName, command.LastName, command.UserId);
        return staffMember;
    }

    public async Task Handle(AssignStaffZoneCommand command)
    {
        var staffMember = await staffMemberRepository.FindByIdAsync(
            Shared.Domain.Model.ValueObjects.StaffMemberId.From(command.StaffMemberId))
            ?? throw new ArgumentException($"Staff member {command.StaffMemberId} not found.");

        var zone = StaffZone.Create(command.Region, command.CenterLatitude, command.CenterLongitude, command.RadiusKm);
        staffMember.AssignZone(zone);

        await unitOfWork.CompleteAsync();

        foreach (var domainEvent in staffMember.DomainEvents)
            await mediator.Publish(domainEvent, CancellationToken.None);
        staffMember.ClearDomainEvents();

        logger.LogInformation("[Profiles] Staff member {Id} assigned to zone {Region}", command.StaffMemberId, command.Region);
    }

    public async Task Handle(UpdateStaffMemberDataCommand command)
    {
        var staffMember = await staffMemberRepository.FindByIdAsync(
            Shared.Domain.Model.ValueObjects.StaffMemberId.From(command.StaffMemberId))
            ?? throw new ArgumentException($"Staff member {command.StaffMemberId} not found.");

        staffMember.UpdatePersonalData(command.FirstName, command.LastName, command.PhoneNumber);
        await unitOfWork.CompleteAsync();

        logger.LogInformation("[Profiles] Staff member {Id} data updated", command.StaffMemberId);
    }

    public async Task Handle(DeactivateStaffMemberCommand command)
    {
        var staffMember = await staffMemberRepository.FindByIdAsync(
            Shared.Domain.Model.ValueObjects.StaffMemberId.From(command.StaffMemberId))
            ?? throw new ArgumentException($"Staff member {command.StaffMemberId} not found.");

        if (command.Reactivate)
            staffMember.Reactivate();
        else
            staffMember.Deactivate();

        await unitOfWork.CompleteAsync();

        foreach (var domainEvent in staffMember.DomainEvents)
            await mediator.Publish(domainEvent, CancellationToken.None);
        staffMember.ClearDomainEvents();

        logger.LogInformation("[Profiles] Staff member {Id} active status changed to {IsActive}",
            command.StaffMemberId, command.Reactivate);
    }

    public async Task Handle(GrantStaffIoTCertificationCommand command)
    {
        var staffMember = await staffMemberRepository.FindByIdAsync(
            Shared.Domain.Model.ValueObjects.StaffMemberId.From(command.StaffMemberId))
            ?? throw new ArgumentException($"Staff member {command.StaffMemberId} not found.");

        staffMember.GrantIoTCertification();
        await unitOfWork.CompleteAsync();

        foreach (var domainEvent in staffMember.DomainEvents)
            await mediator.Publish(domainEvent, CancellationToken.None);
        staffMember.ClearDomainEvents();

        logger.LogInformation("[Profiles] IoT certification granted to staff member {Id}", command.StaffMemberId);
    }

    public async Task Handle(RevokeStaffIoTCertificationCommand command)
    {
        var staffMember = await staffMemberRepository.FindByIdAsync(
            Shared.Domain.Model.ValueObjects.StaffMemberId.From(command.StaffMemberId))
            ?? throw new ArgumentException($"Staff member {command.StaffMemberId} not found.");

        staffMember.RevokeIoTCertification();
        await unitOfWork.CompleteAsync();

        foreach (var domainEvent in staffMember.DomainEvents)
            await mediator.Publish(domainEvent, CancellationToken.None);
        staffMember.ClearDomainEvents();

        logger.LogInformation("[Profiles] IoT certification revoked from staff member {Id}", command.StaffMemberId);
    }
}
