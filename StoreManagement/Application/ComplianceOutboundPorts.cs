using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.StoreManagement.Application;

public sealed record ComplianceEquipment(string EquipmentId, string Name, string AreaName);
public sealed record ComplianceStoreProfile(string StoreId, string Name, string Address, int AreaCount, IReadOnlyList<ComplianceEquipment> Equipment);

/// <summary>Store identity and equipment as needed by compliance records and the OHS dossier.</summary>
public sealed class ComplianceStoreProfiles(StoreHierarchyService hierarchy, IStoreDashboardReadRepository equipment, IStoreHierarchyRepository stores)
{
    /// <exception cref="HierarchyNotFoundException">Store missing or not owned by the actor.</exception>
    public async Task<ComplianceStoreProfile> Profile(UserId actor, string storeId, CancellationToken ct)
    {
        var store = await hierarchy.GetStore(actor, storeId, ct);
        var rows = await equipment.EquipmentStatus(actor, storeId, ct);
        return new(store.Id, store.Name, store.Address, rows.Select(r => r.AreaId).Distinct().Count(),
            rows.Select(r => new ComplianceEquipment(r.EquipmentId, r.Name, r.AreaName)).ToList());
    }

    /// <summary>Equipment of the actor's store, or null when it does not exist there.</summary>
    public async Task<ComplianceEquipment?> EquipmentInStore(UserId actor, string storeId, string equipmentId, CancellationToken ct)
    {
        try
        {
            var item = await hierarchy.GetEquipmentInStore(actor, storeId, equipmentId, ct);
            var area = await hierarchy.GetArea(actor, storeId, item.AreaId, ct);
            return new(item.Id, item.Name, area.Name);
        }
        catch (Exception e) when (e is HierarchyNotFoundException or ArgumentException) { return null; }
    }

    /// <summary>Store name for internal jobs (no authorization), or null.</summary>
    public Task<string?> StoreName(string storeId, CancellationToken ct) => stores.StoreNameAsync(storeId, ct);
}

/// <summary>Safety alert of a store as recorded by AlertManagement.</summary>
public sealed record SafetyAlertRecord(string AlertId, string EquipmentId, string EquipmentName, string AreaName, string AnomalyType,
    string Severity, string Status, DateTime CreatedAtUtc, DateTime? AcknowledgedAtUtc, DateTime? AssignedAtUtc, string? AssignedTechnicianName,
    DateTime? ResolvedAtUtc, string? ResolvedByUserId, string? ResolutionNotes, string? OnDutyWorkerName);

/// <summary>Outbound port to AlertManagement: alert history of a store (the caller authorizes the store first).</summary>
public interface IComplianceAlertPort
{
    Task<IReadOnlyList<SafetyAlertRecord>> History(string storeId, DateTime fromUtc, DateTime toUtc, bool criticalOnly, CancellationToken ct);
}

/// <summary>Outbound port: decodes a signature image to prove it is usable in documents.</summary>
public interface ISignatureImageReader
{
    /// <exception cref="ArgumentException">Not a supported PNG image.</exception>
    (int Width, int Height) Inspect(byte[] png);
}

public sealed record OhsCertificateLine(string EquipmentName, string AreaName, string? CertificationNumber, string? TechnicianName,
    DateTime? IssuedAtUtc, DateTime? ExpiresAtUtc, string Urgency);
public sealed record OhsSignature(string SignerName, string SignerTitle, DateTime SignedAtUtc, byte[] ImagePng);

/// <summary>Content of the occupational health and safety dossier, independent of the file format.</summary>
public sealed record OhsReportData(ComplianceStoreProfile Store, DateTime FromUtc, DateTime ToUtc, DateTime GeneratedAtUtc, string GeneratedByUserId,
    ComplianceScore Score, IReadOnlyList<WeeklySafetyChecklist> Checklists, IReadOnlyList<OhsCertificateLine> Certificates,
    IReadOnlyList<SafetyAlertRecord> ResolvedAlerts, int OpenCriticalAlerts, OhsSignature? Signature);

public sealed record GeneratedDocument(string FileName, string ContentType, byte[] Content);

/// <summary>Outbound port: renders the dossier into an audit-ready file.</summary>
public interface IOhsReportGenerator
{
    GeneratedDocument Generate(OhsReportData data);
}
