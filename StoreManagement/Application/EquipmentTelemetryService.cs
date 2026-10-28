using Hampcoders.Electrolink.API.AlertManagement.Application;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;
using Hampcoders.Electrolink.API.Processing.Domain.Services;
using Hampcoders.Electrolink.API.Processing.Interfaces.REST.Resources;
using Microsoft.Extensions.Configuration;
namespace Hampcoders.Electrolink.API.StoreManagement.Application;

public sealed record DeviceBindingResource(string Id, string EquipmentId, string DeviceId, string SerialNumber,
    string EdgeDeviceId, DateTime LinkedAtUtc, string ThresholdProvenance);
public sealed record EquipmentMeasurementResource(string ReadingId, DateTime CapturedAtUtc, DateTime ReceivedAtUtc,
    string TimestampBasis, float Voltage, float Current, float PowerFactor, float Frequency, string Source,
    float ActivePowerKw, string? DetectedAnomalyType, string? DetectedSeverity);
public sealed record EquipmentTelemetryResource(string EquipmentId, string State, DeviceBindingResource? Binding,
    EquipmentMeasurementResource? LastMeasurement);

public sealed class EquipmentTelemetryService(StoreHierarchyService hierarchy, IEquipmentTelemetryRepository repository,
    IDeviceLinkAuthority authority, IConfiguration configuration, AlertMaterializer alerts)
{
    public async Task<DeviceBindingResource> Link(UserId actor, string storeId, string areaId, string equipmentId,
        string deviceId, string edgeDeviceId, CancellationToken ct)
    {
        await hierarchy.GetEquipment(actor, storeId, areaId, equipmentId, ct);
        var proof = await authority.Verify(actor, deviceId, edgeDeviceId) ?? throw new EquipmentAccessException();
        if (await repository.ByEquipment(equipmentId, ct) != null || await repository.ByDevice(proof.DeviceId, ct) != null
            || await repository.ByEdgeId(edgeDeviceId, ct) != null) throw new EquipmentConflictException();
        var link = EquipmentDeviceBinding.Link(equipmentId, actor, proof.DeviceId, proof.SerialNumber, edgeDeviceId, proof.PropertyId, proof.ProfileId);
        repository.Add(link); await repository.Save(ct); return Resource(link);
    }
    public async Task<DeviceBindingResource> GetBinding(UserId actor, string storeId, string areaId, string equipmentId, CancellationToken ct)
    {
        await hierarchy.GetEquipment(actor, storeId, areaId, equipmentId, ct);
        return Resource(await repository.ByEquipment(equipmentId, ct) ?? throw new HierarchyNotFoundException());
    }
    public async Task<EquipmentTelemetryResource> GetState(UserId actor, string storeId, string areaId, string equipmentId, CancellationToken ct)
    {
        await hierarchy.GetEquipment(actor, storeId, areaId, equipmentId, ct);
        var link = await repository.ByEquipment(equipmentId, ct);
        var last = link == null ? null : (await repository.Recent(link.Id, ct)).FirstOrDefault();
        var proof = link == null ? null : await authority.Verify(link.OwnerUserId, link.DeviceId, link.EdgeDeviceId);
        var verified = proof != null && proof.SerialNumber == link!.SerialNumber && proof.PropertyId == link.LegacyPropertyId && proof.ProfileId == link.AuthorizedProfileId;
        var state = last == null ? "UNKNOWN" : !verified ? "DEVICE_UNVERIFIED" : DateTime.UtcNow - last.CapturedAtUtc > TimeSpan.FromMinutes(link!.Thresholds.DisconnectionThresholdMin)
            ? "STALE" : last.AnomalyType == null ? "NORMAL" : "ANOMALY";
        return new(equipmentId, state, link == null ? null : Resource(link), last == null ? null : new(last.ReadingId,
            last.CapturedAtUtc, last.ReceivedAtUtc, last.TimestampBasis, last.Voltage, last.Current, last.PowerFactor,
            last.Frequency, last.Source, last.AsReading().ActivePowerKw, last.AnomalyType, last.Severity));
    }
    public async Task<HierarchyPage<MeasurementHistoryResource>> GetHistory(UserId actor, string storeId, string areaId,
        string equipmentId, MeasurementHistoryQuery query, CancellationToken ct)
    {
        await hierarchy.GetEquipment(actor, storeId, areaId, equipmentId, ct);
        // Stored history is authorized by equipment ancestry, independently of current ingestion eligibility.
        var rows = await repository.History(actor, storeId, areaId, equipmentId, query.FromUtc, query.ToUtc,
            query.Offset, query.PageSize + 1, ct);
        return new(rows.Take(query.PageSize).Select(r => new MeasurementHistoryResource(r.ReadingId, r.CapturedAtUtc,
            r.ReceivedAtUtc, r.TimestampBasis, r.Voltage, r.Current, r.PowerFactor, r.Frequency, r.Source,
            r.AnomalyType, r.Severity)).ToArray(), query.Page, query.PageSize, rows.Count > query.PageSize);
    }
    private bool InScope(string edgeId) => !string.IsNullOrWhiteSpace(edgeId) && configuration
        .GetSection("EdgeApi:EquipmentReadings:AllowedDeviceIds").GetChildren().Any(c => string.Equals(c.Value, edgeId, StringComparison.Ordinal));
    private async Task<EquipmentDeviceBinding?> MachineLink(string edgeId, CancellationToken ct)
    {
        var link = await repository.ByEdgeId(edgeId, ct);
        if (link == null) { if (InScope(edgeId) || await repository.HasOtherIdentity(edgeId, ct)) throw new EquipmentAccessException(); return null; }
        if (!InScope(edgeId)) throw new EquipmentAccessException();
        var proof = await authority.Verify(link.OwnerUserId, link.DeviceId, link.EdgeDeviceId);
        if (proof == null || proof.SerialNumber != link.SerialNumber || proof.PropertyId != link.LegacyPropertyId
            || proof.ProfileId != link.AuthorizedProfileId) throw new EquipmentAccessException();
        return link;
    }
    // null/false means existing unbound legacy path. Bound devices can never fall back to demo ingestion.
    public async Task<bool> TryIngest(IngestReadingResource input, CancellationToken ct)
    {
        var link = await MachineLink(input.DeviceId, ct);
        if (link == null) return false;
        var reading = EquipmentMeasurement.Capture(link, input.ReadingId, input.Timestamp, input.Voltage, input.Current,
            input.PowerFactor, input.Frequency, input.Source, DateTime.UtcNow);
        var existing = await repository.FindReading(link.Id, input.ReadingId, ct);
        if (existing != null) { if (!existing.SamePayload(reading)) throw new EquipmentConflictException(); return true; }
        if (DateTime.UtcNow - reading.CapturedAtUtc <= TimeSpan.FromMinutes(link.Thresholds.DisconnectionThresholdMin))
        {
            var window = (await repository.Recent(link.Id, ct)).Where(r => r.CapturedAtUtc <= reading.CapturedAtUtc)
                .Take(119).Select(r => r.AsReading()).Append(reading.AsReading()).ToArray();
            var detection = ElectricalAnomalyRules.Detect(reading.AsReading(), window, link.Thresholds);
            reading.RecordDetection(detection?.type.ToString(), detection?.severity.ToString());
            if (detection != null)
                await alerts.Stage(new(link.Id, reading.ReadingId, link.EquipmentId, link.OwnerUserId,
                    reading.AnomalyType!, reading.Severity!, reading.CapturedAtUtc, reading.ReceivedAtUtc), ct);
        }
        repository.Add(reading); await repository.Save(ct); return true;
    }
    public async Task<DeviceThresholdResource?> TryThresholds(string edgeId, CancellationToken ct)
    {
        var link = await MachineLink(edgeId, ct); if (link == null) return null;
        var t = link.Thresholds;
        return new(edgeId, new(t.NormalLimitAmps, t.AlertLimitAmps, t.NominalVoltage, t.MaxConsumptionWatts,
            t.MaxCurrentAmps, t.MinPowerFactor, t.NominalFrequency, t.DisconnectionThresholdMin));
    }
    private static DeviceBindingResource Resource(EquipmentDeviceBinding b) => new(b.Id, b.EquipmentId, b.DeviceId,
        b.SerialNumber, b.EdgeDeviceId, b.LinkedAtUtc, b.ThresholdProvenance);
}
