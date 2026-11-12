using System.Text.Json;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Processing.Domain.Repositories;
using Hampcoders.Electrolink.API.Processing.Domain.Services;
using Hampcoders.Electrolink.API.Processing.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using MediatR;

namespace Hampcoders.Electrolink.API.Processing.Application.Internal.CommandServices;

public class RelayCommandService(
    IRelayControlCommandRepository relayRepository,
    IDeviceReadingStreamRepository streamRepository,
    IAssetsContextFacade           assetsFacade,
    IProfilesContextFacade         profilesFacade,
    IServiceOperationContextFacade serviceOpFacade,
    IMqttSubscriptionService       mqttService,
    IUnitOfWork                    unitOfWork,
    IMediator                      mediator,
    ILogger<RelayCommandService>   logger)
    : IRelayCommandService
{
    public async Task Handle(IssueRelayCommandCommand command)
    {
        bool isSystemCommand = command.TechnicianId == "SYSTEM";

        if (!isSystemCommand)
        {
            bool isCertified = await profilesFacade.IsTechnicianIoTCertifiedAsync(command.TechnicianId);
            if (!isCertified)
                throw new UnauthorizedAccessException(
                    $"Technician {command.TechnicianId} does not have IoT certification.");

            bool hasActiveService = await serviceOpFacade.HasActiveServiceForPropertyAsync(
                command.PropertyId, command.TechnicianId);
            if (!hasActiveService)
                throw new UnauthorizedAccessException(
                    $"No active IN_PROGRESS service found for property {command.PropertyId} " +
                    $"assigned to technician {command.TechnicianId}.");

            // The service check above only covers the requested property, so the device must actually belong to it.
            var stream = await streamRepository.FindByDeviceIdAsync(command.DeviceId);
            if (stream is null || stream.PropertyId.Value != command.PropertyId)
                throw new UnauthorizedAccessException(
                    $"Device {command.DeviceId} does not belong to property {command.PropertyId}.");
        }

        var pending = await relayRepository.FindPendingByDeviceAsync(command.DeviceId);
        if (pending is not null)
            throw new InvalidOperationException(
                $"Device {command.DeviceId} already has a pending relay command ({pending.CommandId.Value}). " +
                "Wait for it to complete before issuing a new one.");

        RelayControlCommand relayCmd = isSystemCommand
            ? RelayControlCommand.IssueBySystem(
                DeviceId.From(command.DeviceId),
                PropertyId.From(command.PropertyId),
                Enum.Parse<ERelayTarget>(command.TargetRelayState))
            : RelayControlCommand.IssueByTechnician(
                DeviceId.From(command.DeviceId),
                PropertyId.From(command.PropertyId),
                Enum.Parse<ERelayTarget>(command.TargetRelayState),
                TechnicianId.From(command.TechnicianId),
                ServiceRequestId.From(command.ServiceRequestId));

        await relayRepository.AddAsync(relayCmd);
        await unitOfWork.CompleteAsync();

        relayCmd.MarkAsSent();
        await relayRepository.UpdateAsync(relayCmd);
        await unitOfWork.CompleteAsync();

        foreach (var ev in relayCmd.DomainEvents)
            await mediator.Publish(ev, CancellationToken.None);
        relayCmd.ClearDomainEvents();

        // Publicar comando por MQTT al dispositivo físico
        var mqttPayload = JsonSerializer.Serialize(new
        {
            commandId = relayCmd.CommandId.Value,
            targetState = command.TargetRelayState,
            issuedAt = DateTime.UtcNow
        });
        await mqttService.PublishCommandAsync(command.DeviceId, mqttPayload);

        logger.LogInformation(
            "[IoT] RelayCommandIssued — DeviceId: {DeviceId} | Target: {Target} | By: {By} | MQTT: published",
            command.DeviceId, command.TargetRelayState, command.TechnicianId);
        // Unanswered commands are timed out by RelayCommandTimeoutWorker (restart-safe, supervised).
    }

    public async Task Handle(AcknowledgeRelayExecutionCommand command)
    {
        var relayCmd = await relayRepository.FindByCommandIdAsync(command.CommandId)
            ?? throw new KeyNotFoundException($"RelayCommand {command.CommandId} not found.");

        if (command.ExecutedSuccessfully)
            relayCmd.MarkAsExecuted();
        else
            relayCmd.MarkAsFailed("Device reported execution failure.");

        await relayRepository.UpdateAsync(relayCmd);
        await unitOfWork.CompleteAsync();

        foreach (var ev in relayCmd.DomainEvents)
            await mediator.Publish(ev, CancellationToken.None);
        relayCmd.ClearDomainEvents();

        logger.LogInformation(
            "[IoT] RelayCommand {CommandId} → {Status}",
            command.CommandId, relayCmd.Status);
    }
}
