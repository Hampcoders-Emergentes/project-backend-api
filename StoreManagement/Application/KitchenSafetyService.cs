using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;

namespace Hampcoders.Electrolink.API.StoreManagement.Application;

public sealed record GetEquipmentSafetyStatusQuery(UserId Actor, string StoreId, string AreaId, string EquipmentId);
public sealed record GetShutdownProtocolQuery(UserId Actor, string StoreId, string AreaId, string EquipmentId);
public sealed record UpdateShutdownProtocolCommand(UserId Actor, string StoreId, string AreaId, string EquipmentId,
    IReadOnlyList<string> ShutdownSteps, IReadOnlyList<string> EmergencyIsolationSteps, SafeToCleanRuleResource Rule);
public sealed record GetElectricalIncidentGuideQuery(UserId Actor, string StoreId);

public sealed record SafeToCleanRuleResource(float MaxDeEnergizedCurrentAmps, int MaxReadingAgeMinutes, bool RequireNoActiveCriticalAlerts);

/// <param name="Status">SAFE_TO_CLEAN, ENERGIZED, HAZARD or UNVERIFIED.</param>
public sealed record EquipmentSafetyStatusResource(string EquipmentId, string Name, string Status, bool IsDeEnergized, bool IsSafeToClean,
    IReadOnlyList<string> Reasons, int ActiveCriticalAlerts, ElectricalReadingResource? LastReading, SafeToCleanRuleResource Rule,
    DateTime EvaluatedAtUtc);

/// <param name="IsDefault">True when the equipment has no specific protocol and the generic one applies.</param>
public sealed record ShutdownProtocolResource(string EquipmentId, string Name, bool IsDefault, IReadOnlyList<string> ShutdownSteps,
    IReadOnlyList<string> EmergencyIsolationSteps, SafeToCleanRuleResource IsSafeToCleanRule, DateTime? UpdatedAtUtc);

public sealed record ElectricalIncidentGuideResource(string StoreId, string StoreName, string StoreAddress, string Version,
    IReadOnlyList<SafetyGuideSection> Sections);

/// <summary>Kitchen sanitation safety: de-energization checks, shutdown protocols and the electrical incident guide.</summary>
public sealed class KitchenSafetyService(StoreHierarchyService hierarchy, IKitchenOperationsRepository repository,
    IStoreDashboardReadRepository readRepository, IStoreAlertSummaryPort alertSummary)
{
    public async Task<EquipmentSafetyStatusResource> Handle(GetEquipmentSafetyStatusQuery query, CancellationToken ct)
    {
        await hierarchy.GetEquipment(query.Actor, query.StoreId, query.AreaId, query.EquipmentId, ct);
        var row = (await readRepository.LiveEquipment(query.Actor, query.StoreId, ct)).SingleOrDefault(e => e.EquipmentId == query.EquipmentId)
                  ?? throw new HierarchyNotFoundException();
        var protocol = await repository.FindProtocol(query.EquipmentId, ct) ?? EquipmentProtocol.Default(query.EquipmentId);
        var alerts = await alertSummary.OpenAlertCountsByEquipment(query.Actor, query.StoreId, ct);
        var critical = alerts.TryGetValue(query.EquipmentId, out var counts) ? counts.Critical : 0;
        var now = DateTime.UtcNow;
        var assessment = SafeToCleanPolicy.Evaluate(protocol.IsSafeToCleanRule, row.Sensor != null, row.Last, row.Thresholds, critical, now);
        return new(row.EquipmentId, row.Name, assessment.Status, assessment.IsDeEnergized, assessment.IsSafeToClean, assessment.Reasons,
            critical, StoreOperationsService.Reading(row.Last), Rule(protocol.IsSafeToCleanRule), now);
    }

    public async Task<ShutdownProtocolResource> Handle(GetShutdownProtocolQuery query, CancellationToken ct)
    {
        var equipment = await hierarchy.GetEquipment(query.Actor, query.StoreId, query.AreaId, query.EquipmentId, ct);
        return Resource(equipment.Name, await repository.FindProtocol(query.EquipmentId, ct) ?? EquipmentProtocol.Default(query.EquipmentId));
    }

    public async Task<ShutdownProtocolResource> Handle(UpdateShutdownProtocolCommand command, CancellationToken ct)
    {
        var equipment = await hierarchy.GetEquipment(command.Actor, command.StoreId, command.AreaId, command.EquipmentId, ct);
        var rule = SafeToCleanRule.Create(command.Rule.MaxDeEnergizedCurrentAmps, command.Rule.MaxReadingAgeMinutes,
            command.Rule.RequireNoActiveCriticalAlerts);
        var protocol = await repository.FindProtocol(command.EquipmentId, ct);
        if (protocol == null)
        {
            protocol = EquipmentProtocol.Default(command.EquipmentId);
            repository.Add(protocol);
        }
        protocol.Update(command.ShutdownSteps, command.EmergencyIsolationSteps, rule, command.Actor.Value, DateTime.UtcNow);
        await repository.Save(ct);
        return Resource(equipment.Name, protocol);
    }

    public async Task<ElectricalIncidentGuideResource> Handle(GetElectricalIncidentGuideQuery query, CancellationToken ct)
    {
        var store = await hierarchy.GetStore(query.Actor, query.StoreId, ct);
        return new(store.Id, store.Name, store.Address, ElectricalIncidentGuide.Version, ElectricalIncidentGuide.Sections);
    }

    private static ShutdownProtocolResource Resource(string name, EquipmentProtocol p) => new(p.EquipmentId, name, p.IsDefault,
        p.ShutdownSteps, p.EmergencyIsolationSteps, Rule(p.IsSafeToCleanRule), p.UpdatedAtUtc);

    private static SafeToCleanRuleResource Rule(SafeToCleanRule r) =>
        new(r.MaxDeEnergizedCurrentAmps, r.MaxReadingAgeMinutes, r.RequireNoActiveCriticalAlerts);
}
