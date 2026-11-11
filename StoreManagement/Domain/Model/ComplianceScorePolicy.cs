namespace Hampcoders.Electrolink.API.StoreManagement.Domain.Model;

public static class ComplianceGrade
{
    public const string Compliant = "COMPLIANT";
    public const string AtRisk = "AT_RISK";
    public const string NonCompliant = "NON_COMPLIANT";
}

/// <param name="WeeklyChecklistScores">Best submitted score of each of the weeks evaluated; null for a week without a submitted checklist.</param>
/// <param name="EquipmentCount">Equipment of the store.</param>
/// <param name="EquipmentWithValidCertificate">Equipment with a maintenance certificate in force.</param>
/// <param name="CriticalAlerts">Critical alerts raised in the evaluation window.</param>
/// <param name="ResolvedCriticalAlerts">Of those, the ones resolved.</param>
public sealed record ComplianceInputs(IReadOnlyList<double?> WeeklyChecklistScores, int EquipmentCount, int EquipmentWithValidCertificate,
    int CriticalAlerts, int ResolvedCriticalAlerts);

public sealed record ComplianceComponent(string Name, double Weight, double Score, string Explanation);

public sealed record ComplianceScore(double Score, string Grade, IReadOnlyList<ComplianceComponent> Components);

/// <summary>
///     Domain service: facility health score (0-100). Weighted average of
///     weekly checklists (40%: mean of the weekly scores, a missing week counts as 0),
///     maintenance certificates (30%: share of equipment with a certificate in force) and
///     critical alarm handling (30%: share of critical alerts resolved; 100 when there were none).
///     COMPLIANT from 80, AT_RISK from 60, NON_COMPLIANT below.
/// </summary>
public static class ComplianceScorePolicy
{
    public const double ChecklistWeight = 0.4, CertificateWeight = 0.3, AlarmWeight = 0.3;

    public static ComplianceScore Evaluate(ComplianceInputs inputs)
    {
        var weeks = inputs.WeeklyChecklistScores;
        var checklist = weeks.Count == 0 ? 0 : weeks.Average(s => s ?? 0);
        var submitted = weeks.Count(s => s != null);
        var certificates = inputs.EquipmentCount == 0 ? 100 : inputs.EquipmentWithValidCertificate * 100.0 / inputs.EquipmentCount;
        var alarms = inputs.CriticalAlerts == 0 ? 100 : inputs.ResolvedCriticalAlerts * 100.0 / inputs.CriticalAlerts;
        var components = new List<ComplianceComponent>
        {
            new("WEEKLY_CHECKLISTS", ChecklistWeight, Math.Round(checklist, 2),
                $"{submitted} of {weeks.Count} week(s) with a submitted checklist; missing weeks count as 0."),
            new("MAINTENANCE_CERTIFICATES", CertificateWeight, Math.Round(certificates, 2),
                $"{inputs.EquipmentWithValidCertificate} of {inputs.EquipmentCount} equipment with a certificate in force."),
            new("CRITICAL_ALARMS_RESOLVED", AlarmWeight, Math.Round(alarms, 2),
                $"{inputs.ResolvedCriticalAlerts} of {inputs.CriticalAlerts} critical alert(s) resolved.")
        };
        var score = Math.Round(components.Sum(c => c.Weight * c.Score), 2);
        var grade = score >= 80 ? ComplianceGrade.Compliant : score >= 60 ? ComplianceGrade.AtRisk : ComplianceGrade.NonCompliant;
        return new(score, grade, components);
    }
}
