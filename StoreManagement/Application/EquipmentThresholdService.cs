using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;

namespace Hampcoders.Electrolink.API.StoreManagement.Application;

public sealed record EquipmentThresholdsResource(float NominalVoltage, float MaxConsumptionWatts, float MaxCurrentAmps,
    float MinPowerFactor, float NominalFrequency, int DisconnectionThresholdMin, float NormalLimitAmps, float AlertLimitAmps);

public sealed record GetEquipmentThresholdsQuery(UserId Actor, string StoreId, string AreaId, string EquipmentId);
public sealed record UpdateEquipmentThresholdsCommand(UserId Actor, string StoreId, string AreaId, string EquipmentId,
    EquipmentThresholdsResource Thresholds);

/// <summary>View and change the operating limits of one piece of equipment owned by the actor.</summary>
public sealed class EquipmentThresholdService(StoreHierarchyService hierarchy, IStoreHierarchyRepository repository)
{
    public async Task<EquipmentThresholdsResource> Handle(GetEquipmentThresholdsQuery query, CancellationToken ct)
    {
        await hierarchy.GetEquipment(query.Actor, query.StoreId, query.AreaId, query.EquipmentId, ct);
        var equipment = await repository.FindEquipmentAsync(query.Actor, query.StoreId, query.AreaId, query.EquipmentId, ct)
            ?? throw new HierarchyNotFoundException();
        return Resource(equipment.Thresholds);
    }

    public async Task<EquipmentThresholdsResource> Handle(UpdateEquipmentThresholdsCommand command, CancellationToken ct)
    {
        await hierarchy.GetEquipment(command.Actor, command.StoreId, command.AreaId, command.EquipmentId, ct);
        var equipment = await repository.FindEquipmentAsync(command.Actor, command.StoreId, command.AreaId, command.EquipmentId, ct)
            ?? throw new HierarchyNotFoundException();
        var t = command.Thresholds;
        equipment.UpdateThresholds(ThresholdConfig.Create(t.NominalVoltage, t.MaxConsumptionWatts, t.MaxCurrentAmps,
            t.MinPowerFactor, t.NominalFrequency, t.DisconnectionThresholdMin, t.NormalLimitAmps, t.AlertLimitAmps));
        repository.Update(equipment);
        await repository.SaveAsync(ct);
        return Resource(equipment.Thresholds);
    }

    private static EquipmentThresholdsResource Resource(ThresholdConfig t) => new(t.NominalVoltage, t.MaxConsumptionWatts,
        t.MaxCurrentAmps, t.MinPowerFactor, t.NominalFrequency, t.DisconnectionThresholdMin, t.NormalLimitAmps, t.AlertLimitAmps);
}
