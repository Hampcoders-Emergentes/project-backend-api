using System.Globalization;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.StoreManagement.Application;

public sealed record GetComplianceScoreQuery(UserId Actor, string StoreId);
public sealed record GetCertificateExpirationsQuery(UserId Actor, string StoreId, int WithinDays);
public sealed record GenerateOhsReportCommand(UserId Actor, string StoreId, DateTime FromUtc, DateTime ToUtc);
public sealed record GetOhsReportFileQuery(UserId Actor, string StoreId, string ReportId);
public sealed record ListOhsReportsQuery(UserId Actor, string StoreId);

public sealed record ChecklistWeekResource(DateOnly WeekStartDate, double? BestSubmittedScore);
public sealed record ComplianceScoreResource(string StoreId, double Score, string Grade, IReadOnlyList<ComplianceComponent> Components,
    IReadOnlyList<ChecklistWeekResource> ChecklistWeeks, DateTime AlarmWindowFromUtc, DateTime EvaluatedAtUtc);

public sealed record UncertifiedEquipmentResource(string EquipmentId, string Name, string AreaName);
/// <param name="Items">Latest certificate of each equipment that expires within the window (or already expired), soonest first.</param>
public sealed record CertificateExpirationsResource(string StoreId, DateTime GeneratedAtUtc, int WithinDays,
    IReadOnlyList<MaintenanceCertificateResource> Items, IReadOnlyList<UncertifiedEquipmentResource> EquipmentWithoutCertificate);

public sealed record OhsReportMetadata(string ReportId, string StoreId, string FileName, string ContentType, long SizeBytes, DateTime FromUtc,
    DateTime ToUtc, DateTime GeneratedAtUtc, double ComplianceScore, bool IsSigned);

/// <summary>Compliance score, certificate expirations and the official OHS dossier of a store.</summary>
public sealed class ComplianceAssessmentService(ComplianceStoreProfiles stores, IComplianceRepository repository, IComplianceAlertPort alerts,
    IOhsReportGenerator generator)
{
    public const int ChecklistWeeksEvaluated = 4;
    public static readonly TimeSpan AlarmWindow = TimeSpan.FromDays(90);

    public async Task<ComplianceScoreResource> Handle(GetComplianceScoreQuery query, CancellationToken ct)
    {
        var profile = await stores.Profile(query.Actor, query.StoreId, ct);
        var now = DateTime.UtcNow;
        var (score, weeks) = await Score(profile, now, ct);
        return new(query.StoreId, score.Score, score.Grade, score.Components, weeks, now - AlarmWindow, now);
    }

    public async Task<CertificateExpirationsResource> Handle(GetCertificateExpirationsQuery query, CancellationToken ct)
    {
        if (query.WithinDays is < 1 or > 90) throw new ArgumentException("withinDays must be between 1 and 90.");
        var profile = await stores.Profile(query.Actor, query.StoreId, ct);
        var now = DateTime.UtcNow;
        var latest = LatestPerEquipment(await repository.Certificates(query.StoreId, ct));
        var equipment = profile.Equipment.ToDictionary(e => e.EquipmentId);
        var expiring = latest.Values.Where(c => c.ExpiresAtUtc <= now.AddDays(query.WithinDays) && equipment.ContainsKey(c.EquipmentId))
            .OrderBy(c => c.ExpiresAtUtc).Select(c => ComplianceRecordsService.Certificate(c, equipment[c.EquipmentId], now)).ToList();
        var uncertified = profile.Equipment.Where(e => !latest.ContainsKey(e.EquipmentId))
            .Select(e => new UncertifiedEquipmentResource(e.EquipmentId, e.Name, e.AreaName)).ToList();
        return new(query.StoreId, now, query.WithinDays, expiring, uncertified);
    }

    /// <summary>Builds and stores the dossier for [from, to) (at most 366 days).</summary>
    public async Task<OhsReportMetadata> Handle(GenerateOhsReportCommand command, CancellationToken ct)
    {
        if (command.ToUtc <= command.FromUtc || command.ToUtc - command.FromUtc > TimeSpan.FromDays(366))
            throw new ArgumentException("from must precede to; the period must not exceed 366 days.");
        var profile = await stores.Profile(command.Actor, command.StoreId, ct);
        var now = DateTime.UtcNow;
        var (score, _) = await Score(profile, now, ct);
        var checklists = await repository.ListChecklists(command.StoreId, null, DateOnly.FromDateTime(command.FromUtc).AddDays(-6),
            DateOnly.FromDateTime(command.ToUtc), 0, 200, ct);
        var history = await alerts.History(command.StoreId, command.FromUtc, command.ToUtc, criticalOnly: false, ct);
        var latest = LatestPerEquipment(await repository.Certificates(command.StoreId, ct));
        var certificates = profile.Equipment.Select(e => latest.TryGetValue(e.EquipmentId, out var c)
            ? new OhsCertificateLine(e.Name, e.AreaName, c.CertificationNumber, c.TechnicianName, c.IssuedAtUtc, c.ExpiresAtUtc, c.UrgencyAt(now))
            : new OhsCertificateLine(e.Name, e.AreaName, null, null, null, null, "NO_CERTIFICATE")).ToList();
        var signature = await repository.LatestSignature(command.StoreId, ct);
        var data = new OhsReportData(profile, command.FromUtc, command.ToUtc, now, command.Actor.Value, score,
            checklists.OrderBy(c => c.WeekStartDate).ToList(), certificates,
            history.Where(a => a.ResolvedAtUtc != null).OrderBy(a => a.CreatedAtUtc).ToList(),
            history.Count(a => a.Severity == "CRITICAL" && a.ResolvedAtUtc == null),
            signature == null ? null : new OhsSignature(signature.SignerName, signature.SignerTitle, signature.SignedAtUtc, signature.ImagePng));
        var file = generator.Generate(data);
        var report = OhsReport.Create(command.StoreId, command.Actor.Value, file.FileName, file.ContentType, file.Content, command.FromUtc,
            command.ToUtc, score.Score, signature?.Id, now);
        repository.Add(report);
        await repository.Save(ct);
        return Metadata(report);
    }

    public async Task<OhsReport> Handle(GetOhsReportFileQuery query, CancellationToken ct)
    {
        await stores.Profile(query.Actor, query.StoreId, ct);
        return await repository.FindReport(query.StoreId, query.ReportId, ct) ?? throw new HierarchyNotFoundException();
    }

    public async Task<IReadOnlyList<OhsReportMetadata>> Handle(ListOhsReportsQuery query, CancellationToken ct)
    {
        await stores.Profile(query.Actor, query.StoreId, ct);
        return (await repository.ListReports(query.StoreId, 100, ct)).Select(Metadata).ToList();
    }

    private async Task<(ComplianceScore Score, IReadOnlyList<ChecklistWeekResource> Weeks)> Score(ComplianceStoreProfile profile, DateTime now, CancellationToken ct)
    {
        // The last completed weeks: the current week is still in progress.
        var currentWeek = WeeklySafetyChecklist.WeekStart(DateOnly.FromDateTime(now));
        var weekStarts = Enumerable.Range(1, ChecklistWeeksEvaluated).Select(k => currentWeek.AddDays(-7 * k)).OrderBy(d => d).ToList();
        var submitted = await repository.ListChecklists(profile.StoreId, ChecklistStatus.Submitted, weekStarts[0], weekStarts[^1], 0, 500, ct);
        var weeks = weekStarts.Select(w => new ChecklistWeekResource(w,
            submitted.Where(c => c.WeekStartDate == w).Select(c => (double?)c.Score).DefaultIfEmpty(null).Max())).ToList();

        var latest = LatestPerEquipment(await repository.Certificates(profile.StoreId, ct));
        var certified = profile.Equipment.Count(e => latest.TryGetValue(e.EquipmentId, out var c) && c.IsValidAt(now));

        var critical = await alerts.History(profile.StoreId, now - AlarmWindow, now, criticalOnly: true, ct);
        var score = ComplianceScorePolicy.Evaluate(new ComplianceInputs(weeks.Select(w => w.BestSubmittedScore).ToList(), profile.Equipment.Count,
            certified, critical.Count, critical.Count(a => a.ResolvedAtUtc != null)));
        return (score, weeks);
    }

    /// <summary>Certificate with the latest expiry per equipment (a renewal supersedes the previous one).</summary>
    internal static Dictionary<string, EquipmentMaintenanceCertificate> LatestPerEquipment(IEnumerable<EquipmentMaintenanceCertificate> certificates) =>
        certificates.GroupBy(c => c.EquipmentId).ToDictionary(g => g.Key, g => g.MaxBy(c => c.ExpiresAtUtc)!);

    private static OhsReportMetadata Metadata(OhsReport r) => new(r.Id, r.StoreId, r.FileName, r.ContentType, r.SizeBytes, r.FromUtc, r.ToUtc,
        r.GeneratedAtUtc, r.ComplianceScore, r.SignatureId != null);
}

/// <summary>
///     Warns store managers about maintenance certificates expiring in 30 days, in 15 days and on expiry (once each).
///     Only the latest certificate of an equipment is considered, so a renewal silences the old one.
/// </summary>
public sealed class CertificateExpiryNotifier(IComplianceRepository repository, ComplianceStoreProfiles stores, StoreManagerNotifier notifier,
    ILogger<CertificateExpiryNotifier> logger)
{
    public async Task<int> NotifyDue(DateTime nowUtc, CancellationToken ct)
    {
        var horizon = nowUtc.AddDays(EquipmentMaintenanceCertificate.NoticeThresholdsDays.Max());
        var candidates = await repository.CertificatesExpiringBefore(horizon, 500, ct);
        var sent = 0;
        foreach (var storeGroup in candidates.GroupBy(c => c.StoreId))
        {
            var latest = ComplianceAssessmentService.LatestPerEquipment(await repository.Certificates(storeGroup.Key, ct));
            var due = storeGroup.Where(c => latest.TryGetValue(c.EquipmentId, out var l) && l.Id == c.Id)
                .Select(c => (Certificate: c, Threshold: c.DueNotice(nowUtc))).Where(x => x.Threshold != null).ToList();
            if (due.Count == 0) continue;
            var storeName = await stores.StoreName(storeGroup.Key, ct) ?? storeGroup.Key;
            var lines = due.OrderBy(x => x.Certificate.ExpiresAtUtc).Select(x => x.Threshold == 0
                ? $"- Certificate {x.Certificate.CertificationNumber} ({x.Certificate.TechnicianName}) EXPIRED on {Date(x.Certificate.ExpiresAtUtc)}."
                : $"- Certificate {x.Certificate.CertificationNumber} ({x.Certificate.TechnicianName}) expires on {Date(x.Certificate.ExpiresAtUtc)}.").ToList();
            var body = $"Maintenance certificates of {storeName} need attention:\n\n" + string.Join('\n', lines)
                       + "\n\nSchedule the maintenance visit and register the new certificate in Electrolink.";
            var payload = new
            {
                Type = "CERTIFICATE_EXPIRATION", StoreId = storeGroup.Key,
                Certificates = due.Select(x => new { x.Certificate.Id, x.Certificate.EquipmentId, x.Certificate.CertificationNumber, x.Certificate.ExpiresAtUtc,
                    Urgency = x.Certificate.UrgencyAt(nowUtc) }).ToList()
            };
            if (!await notifier.Notify(storeGroup.Key, new StoreManagerNotice($"[Electrolink] Maintenance certificates expiring - {storeName}", body, "ComplianceNotice", payload), ct))
            {
                logger.LogWarning("[Compliance] Expiry notice for store {StoreId} could not be delivered; will retry", storeGroup.Key);
                continue;
            }
            foreach (var (certificate, threshold) in due) certificate.RecordNotice(threshold!.Value);
            await repository.Save(ct);
            sent += due.Count;
        }
        return sent;
    }

    private static string Date(DateTime utc) => utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
