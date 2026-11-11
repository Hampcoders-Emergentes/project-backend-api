using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.StoreManagement.Application;

public sealed record ChecklistItemInput(string Code, string Result, string? Notes);
public sealed record SaveWeeklyChecklistCommand(UserId Actor, string StoreId, string? ChecklistId, string InspectorStaffId, string InspectorName,
    DateOnly? WeekOf, IReadOnlyList<ChecklistItemInput> Items, string? Observations, bool Submit);
public sealed record ListWeeklyChecklistsQuery(UserId Actor, string StoreId, string? Status, DateOnly? FromWeek, DateOnly? ToWeek, int Page, int PageSize);
public sealed record RegisterCertificateCommand(UserId Actor, string StoreId, string EquipmentId, string TechnicianName, string CertificationNumber,
    string? WorkDescription, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, string? DocumentFileUrl);
public sealed record ListCertificatesQuery(UserId Actor, string StoreId, string EquipmentId);
public sealed record RegisterSignatureCommand(UserId Actor, string StoreId, string SignerName, string SignerTitle, string ImageBase64);
public sealed record GetSignatureQuery(UserId Actor, string StoreId);

public sealed record WeeklySafetyChecklistResource(string Id, string StoreId, string InspectorStaffId, string InspectorName, DateOnly WeekStartDate,
    string Week, string Status, double Score, string? Observations, IReadOnlyList<ChecklistItem> Items, DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc, DateTime? SubmittedAtUtc);
public sealed record ChecklistPageResource(IReadOnlyList<WeeklySafetyChecklistResource> Items, int Page, int PageSize, bool HasMore);
public sealed record ChecklistTemplateResource(IReadOnlyList<ChecklistItemDefinition> Items, IReadOnlyList<string> Results);

/// <param name="Urgency">VALID, EXPIRES_WITHIN_30_DAYS, EXPIRES_WITHIN_15_DAYS or EXPIRED.</param>
public sealed record MaintenanceCertificateResource(string Id, string StoreId, string EquipmentId, string EquipmentName, string AreaName,
    string TechnicianName, string CertificationNumber, string? WorkDescription, DateTime IssuedAtUtc, DateTime ExpiresAtUtc,
    string? DocumentFileUrl, string Urgency, int DaysUntilExpiry, DateTime RegisteredAtUtc);

public sealed record ComplianceSignatureResource(string Id, string StoreId, string SignerName, string SignerTitle, string Sha256, int WidthPx,
    int HeightPx, DateTime SignedAtUtc);

/// <summary>The record conflicts with an existing one (duplicate certificate number, submitted checklist).</summary>
public sealed class ComplianceConflictException(string message) : Exception(message);

/// <summary>Records kept for regulatory compliance: weekly checklists, maintenance certificates and the manager's signature.</summary>
public sealed class ComplianceRecordsService(ComplianceStoreProfiles stores, IComplianceRepository repository, ISignatureImageReader images)
{
    public static ChecklistTemplateResource Template() => new(SafetyChecklistCatalog.Items, ChecklistItemResult.All);

    /// <summary>Creates a checklist (draft or submitted) or, with <c>ChecklistId</c>, revises a draft.</summary>
    public async Task<WeeklySafetyChecklistResource> Handle(SaveWeeklyChecklistCommand command, CancellationToken ct)
    {
        await stores.Profile(command.Actor, command.StoreId, ct);
        var now = DateTime.UtcNow;
        var items = (command.Items ?? []).Select(i => new ChecklistItem(i.Code, "", i.Result, i.Notes)).ToList();
        WeeklySafetyChecklist checklist;
        if (command.ChecklistId == null)
        {
            checklist = WeeklySafetyChecklist.Start(command.StoreId, command.InspectorStaffId, command.InspectorName,
                command.WeekOf ?? DateOnly.FromDateTime(now), items, command.Observations, command.Submit, command.Actor.Value, now);
            repository.Add(checklist);
        }
        else
        {
            checklist = await repository.FindChecklist(command.StoreId, command.ChecklistId, ct) ?? throw new HierarchyNotFoundException();
            try { checklist.Revise(command.InspectorStaffId, command.InspectorName, items, command.Observations, command.Submit, now); }
            catch (ChecklistLockedException e) { throw new ComplianceConflictException(e.Message); }
        }
        await repository.Save(ct);
        return Resource(checklist);
    }

    public async Task<ChecklistPageResource> Handle(ListWeeklyChecklistsQuery query, CancellationToken ct)
    {
        await stores.Profile(query.Actor, query.StoreId, ct);
        var status = query.Status?.Trim().ToUpperInvariant();
        if (status != null && !ChecklistStatus.All.Contains(status)) throw new ArgumentException("status must be DRAFT or SUBMITTED.");
        if (query.Page < 1 || query.PageSize is < 1 or > 100 || (long)(query.Page - 1) * query.PageSize > int.MaxValue)
            throw new ArgumentException("page must be positive; pageSize must be between 1 and 100.");
        var rows = await repository.ListChecklists(query.StoreId, status, query.FromWeek, query.ToWeek, (query.Page - 1) * query.PageSize,
            query.PageSize + 1, ct);
        return new(rows.Take(query.PageSize).Select(Resource).ToList(), query.Page, query.PageSize, rows.Count > query.PageSize);
    }

    public async Task<MaintenanceCertificateResource> Handle(RegisterCertificateCommand command, CancellationToken ct)
    {
        var equipment = await RequireEquipment(command.Actor, command.StoreId, command.EquipmentId, ct);
        var now = DateTime.UtcNow;
        var certificate = EquipmentMaintenanceCertificate.Register(command.StoreId, equipment.EquipmentId, command.TechnicianName,
            command.CertificationNumber, command.WorkDescription, command.IssuedAt.UtcDateTime, command.ExpiresAt.UtcDateTime,
            command.DocumentFileUrl, command.Actor.Value, now);
        if (await repository.CertificateNumberExists(equipment.EquipmentId, certificate.CertificationNumber, ct))
            throw new ComplianceConflictException("This certificate number is already registered for the equipment.");
        repository.Add(certificate);
        await repository.Save(ct);
        return Certificate(certificate, equipment, now);
    }

    public async Task<IReadOnlyList<MaintenanceCertificateResource>> Handle(ListCertificatesQuery query, CancellationToken ct)
    {
        var equipment = await RequireEquipment(query.Actor, query.StoreId, query.EquipmentId, ct);
        var now = DateTime.UtcNow;
        return (await repository.Certificates(query.StoreId, ct, equipment.EquipmentId)).Select(c => Certificate(c, equipment, now)).ToList();
    }

    /// <summary>Stores a new signature (base64 PNG, optionally as a data URL); it becomes the one used in documents.</summary>
    public async Task<ComplianceSignatureResource> Handle(RegisterSignatureCommand command, CancellationToken ct)
    {
        await stores.Profile(command.Actor, command.StoreId, ct);
        var png = DecodeBase64Png(command.ImageBase64);
        var (width, height) = images.Inspect(png);
        var signature = ComplianceSignature.Register(command.StoreId, command.SignerName, command.SignerTitle, png, width, height,
            command.Actor.Value, DateTime.UtcNow);
        repository.Add(signature);
        await repository.Save(ct);
        return Signature(signature);
    }

    public async Task<ComplianceSignatureResource> Handle(GetSignatureQuery query, CancellationToken ct)
    {
        await stores.Profile(query.Actor, query.StoreId, ct);
        return Signature(await repository.LatestSignature(query.StoreId, ct) ?? throw new HierarchyNotFoundException());
    }

    public async Task<(byte[] Png, string Sha256)> SignatureImage(GetSignatureQuery query, CancellationToken ct)
    {
        await stores.Profile(query.Actor, query.StoreId, ct);
        var signature = await repository.LatestSignature(query.StoreId, ct) ?? throw new HierarchyNotFoundException();
        return (signature.ImagePng, signature.Sha256);
    }

    private async Task<ComplianceEquipment> RequireEquipment(UserId actor, string storeId, string equipmentId, CancellationToken ct)
    {
        await stores.Profile(actor, storeId, ct);
        return await stores.EquipmentInStore(actor, storeId, equipmentId, ct) ?? throw new HierarchyNotFoundException();
    }

    private static byte[] DecodeBase64Png(string value)
    {
        var text = (value ?? "").Trim();
        const string prefix = "data:image/png;base64,";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) text = text[prefix.Length..];
        if (text.Length == 0 || text.Length > (ComplianceSignature.MaxImageBytes + 2) / 3 * 4 + 4)
            throw new ArgumentException($"imageBase64 must contain a PNG of at most {ComplianceSignature.MaxImageBytes / 1024} KB.");
        var buffer = new byte[text.Length * 3 / 4 + 3];
        if (!Convert.TryFromBase64String(text, buffer, out var written)) throw new ArgumentException("imageBase64 is not valid base64.");
        return buffer[..written];
    }

    internal static WeeklySafetyChecklistResource Resource(WeeklySafetyChecklist c) => new(c.Id, c.StoreId, c.InspectorStaffId, c.InspectorName,
        c.WeekStartDate, c.WeekLabel, c.Status, c.Score, c.Observations, c.Items, c.CreatedAtUtc, c.UpdatedAtUtc, c.SubmittedAtUtc);

    internal static MaintenanceCertificateResource Certificate(EquipmentMaintenanceCertificate c, ComplianceEquipment? e, DateTime now) =>
        new(c.Id, c.StoreId, c.EquipmentId, e?.Name ?? c.EquipmentId, e?.AreaName ?? "", c.TechnicianName, c.CertificationNumber,
            c.WorkDescription, c.IssuedAtUtc, c.ExpiresAtUtc, c.DocumentFileUrl, c.UrgencyAt(now),
            (int)Math.Floor((c.ExpiresAtUtc - now).TotalDays), c.RegisteredAtUtc);

    private static ComplianceSignatureResource Signature(ComplianceSignature s) =>
        new(s.Id, s.StoreId, s.SignerName, s.SignerTitle, s.Sha256, s.WidthPx, s.HeightPx, s.SignedAtUtc);
}
