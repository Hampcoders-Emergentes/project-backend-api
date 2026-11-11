using System.Globalization;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Reporting;

namespace Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Reporting;

/// <summary>
///     Audit-ready occupational health and safety dossier: facility details and compliance score, weekly inspections,
///     maintenance certificates, a chronological log of resolved safety alerts and the store manager's signature.
/// </summary>
public sealed class OhsReportPdfGenerator : IOhsReportGenerator
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public GeneratedDocument Generate(OhsReportData d)
    {
        var pdf = new SimplePdfDocument($"Electrolink OHS compliance dossier - {d.Store.Name} - generated {Ts(d.GeneratedAtUtc)} UTC");
        pdf.Title("Electrical Safety Compliance Dossier");
        pdf.Line("Occupational health and safety - electrical installations of a commercial kitchen");
        pdf.Line();

        pdf.Heading("1. Facility");
        pdf.Line($"Store:            {d.Store.Name}");
        pdf.Line($"Address:          {d.Store.Address}");
        pdf.Line($"Store ID:         {d.Store.StoreId}");
        pdf.Line($"Areas:            {d.Store.AreaCount}");
        pdf.Line($"Equipment:        {d.Store.Equipment.Count}");
        pdf.Line($"Period covered:   {Ts(d.FromUtc)} to {Ts(d.ToUtc)} UTC");
        pdf.Line($"Generated:        {Ts(d.GeneratedAtUtc)} UTC by user {d.GeneratedByUserId}");

        pdf.Heading("2. Compliance score");
        pdf.Line($"Score: {d.Score.Score.ToString("0.0", Inv)} / 100   Grade: {d.Score.Grade}");
        foreach (var c in d.Score.Components)
        {
            pdf.Line($"- {c.Name,-26} weight {c.Weight * 100,3:0}%  score {c.Score.ToString("0.0", Inv),6}");
            foreach (var line in Wrap(c.Explanation, SimplePdfDocument.LineWidth - 4)) pdf.Line("    " + line);
        }
        if (d.OpenCriticalAlerts > 0)
            pdf.Line($"WARNING: {d.OpenCriticalAlerts} critical alert(s) raised in the period are still unresolved.");

        pdf.Heading("3. Weekly electrical network inspections");
        if (d.Checklists.Count == 0) pdf.Line("No inspection was recorded in the period.");
        else
        {
            pdf.Line($"{"Week",-10} {"Inspector",-28} {"Status",-10} {"Score",6}  Failed items");
            foreach (var c in d.Checklists)
            {
                var failed = c.Items.Where(i => i.Result == ChecklistItemResult.Fail).ToList();
                pdf.Line($"{c.WeekLabel,-10} {Clip(c.InspectorName, 28),-28} {c.Status,-10} {c.Score.ToString("0.0", Inv),6}  {failed.Count}");
                foreach (var item in failed)
                    foreach (var line in Wrap($"FAIL {item.Code}: {item.Notes}", SimplePdfDocument.LineWidth - 6)) pdf.Line("      " + line);
            }
        }

        pdf.Heading("4. Maintenance certificates");
        pdf.Line($"{"Equipment",-24} {"Certificate",-18} {"Technician",-20} {"Expires",-10} Status");
        foreach (var c in d.Certificates)
            pdf.Line($"{Clip(c.AreaName + "/" + c.EquipmentName, 24),-24} {Clip(c.CertificationNumber ?? "-", 18),-18} " +
                     $"{Clip(c.TechnicianName ?? "-", 20),-20} {(c.ExpiresAtUtc == null ? "-" : c.ExpiresAtUtc.Value.ToString("yyyy-MM-dd", Inv)),-10} {c.Urgency}");

        pdf.Heading("5. Resolved safety alerts (chronological)");
        if (d.ResolvedAlerts.Count == 0) pdf.Line("No safety alert was resolved in the period.");
        foreach (var a in d.ResolvedAlerts)
        {
            pdf.Line($"{Ts(a.CreatedAtUtc)}  {a.Severity,-12} {a.AnomalyType,-20} {Clip(a.AreaName + "/" + a.EquipmentName, 30)}");
            var trail = new List<string>();
            if (a.AcknowledgedAtUtc != null) trail.Add($"acknowledged {Ts(a.AcknowledgedAtUtc.Value)}");
            if (a.AssignedAtUtc != null) trail.Add($"assigned {Ts(a.AssignedAtUtc.Value)} to {a.AssignedTechnicianName ?? "technician"}");
            trail.Add($"resolved {Ts(a.ResolvedAtUtc!.Value)} by {a.ResolvedByUserId ?? "-"}");
            if (a.OnDutyWorkerName != null) trail.Add($"on duty: {a.OnDutyWorkerName}");
            foreach (var line in Wrap(string.Join("; ", trail), SimplePdfDocument.LineWidth - 4)) pdf.Line("    " + line);
            if (a.ResolutionNotes != null)
                foreach (var line in Wrap("Notes: " + a.ResolutionNotes, SimplePdfDocument.LineWidth - 4)) pdf.Line("    " + line);
        }

        pdf.Heading("6. Declaration");
        foreach (var line in Wrap("I declare that the information in this dossier reflects the inspections, maintenance and incident " +
                                  "handling recorded for this facility during the period covered.", SimplePdfDocument.LineWidth))
            pdf.Line(line);
        pdf.Line();
        if (d.Signature == null) pdf.Line("Signature: not registered.");
        else
        {
            try { pdf.Image(RasterImage.FromPng(d.Signature.ImagePng), 180f, 70f); }
            catch (ArgumentException) { pdf.Line("[signature image could not be rendered]"); }
            pdf.Line("______________________________");
            pdf.Line($"{d.Signature.SignerName}, {d.Signature.SignerTitle}");
            pdf.Line($"Signature registered on {Ts(d.Signature.SignedAtUtc)} UTC");
        }

        var name = $"ohs-dossier-{d.Store.StoreId}-{d.GeneratedAtUtc:yyyyMMddHHmm}.pdf";
        return new GeneratedDocument(name, "application/pdf", pdf.ToBytes());
    }

    private static string Ts(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm", Inv);

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "~";

    private static IEnumerable<string> Wrap(string text, int width)
    {
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width) { yield return line; line = ""; }
            line = line.Length == 0 ? word : line + " " + word;
            while (line.Length > width) { yield return line[..width]; line = line[width..]; }
        }
        if (line.Length > 0) yield return line;
    }
}
