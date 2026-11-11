using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;

namespace Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;

/// <summary>Compliance records of stores; callers authorize the store first (except the expiry job).</summary>
public interface IComplianceRepository
{
    void Add(WeeklySafetyChecklist checklist);
    Task<WeeklySafetyChecklist?> FindChecklist(string storeId, string checklistId, CancellationToken ct);
    /// <summary>Newest week first; optional filters.</summary>
    Task<List<WeeklySafetyChecklist>> ListChecklists(string storeId, string? status, DateOnly? fromWeek, DateOnly? toWeek, int skip, int take, CancellationToken ct);

    void Add(EquipmentMaintenanceCertificate certificate);
    Task<bool> CertificateNumberExists(string equipmentId, string certificationNumber, CancellationToken ct);
    /// <summary>Certificates of the store (optionally one equipment), latest expiry first.</summary>
    Task<List<EquipmentMaintenanceCertificate>> Certificates(string storeId, CancellationToken ct, string? equipmentId = null);
    /// <summary>Tracked certificates of any store expiring before the given time (expiry job).</summary>
    Task<List<EquipmentMaintenanceCertificate>> CertificatesExpiringBefore(DateTime limitUtc, int take, CancellationToken ct);

    void Add(ComplianceSignature signature);
    Task<ComplianceSignature?> LatestSignature(string storeId, CancellationToken ct);

    void Add(OhsReport report);
    Task<OhsReport?> FindReport(string storeId, string reportId, CancellationToken ct);
    /// <summary>Report metadata only (no file content), newest first.</summary>
    Task<List<OhsReport>> ListReports(string storeId, int take, CancellationToken ct);

    Task Save(CancellationToken ct);
}
