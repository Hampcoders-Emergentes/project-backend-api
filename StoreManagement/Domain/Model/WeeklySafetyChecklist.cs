using System.Globalization;
using System.Text.Json;

namespace Hampcoders.Electrolink.API.StoreManagement.Domain.Model;

public static class ChecklistStatus
{
    public const string Draft = "DRAFT";
    public const string Submitted = "SUBMITTED";
    public static readonly IReadOnlyList<string> All = [Draft, Submitted];
}

public static class ChecklistItemResult
{
    public const string Pass = "PASS";
    public const string Fail = "FAIL";
    public const string NotApplicable = "NOT_APPLICABLE";
    public static readonly IReadOnlyList<string> All = [Pass, Fail, NotApplicable];
}

public sealed record ChecklistItemDefinition(string Code, string Label);

/// <summary>Weekly inspection points of a commercial kitchen's electrical network (occupational health and safety).</summary>
public static class SafetyChecklistCatalog
{
    public static readonly IReadOnlyList<ChecklistItemDefinition> Items =
    [
        new("PANEL_ACCESS_CLEAR", "Distribution boards are accessible, closed and free of stored objects."),
        new("PANEL_LABELS", "Every breaker is labelled with the circuit or equipment it feeds."),
        new("RCD_TEST", "Residual current devices (differentials) trip when their test button is pressed."),
        new("NO_EXPOSED_WIRING", "No exposed, damaged or improvised wiring is visible."),
        new("CABLES_AWAY_FROM_WATER_HEAT", "Cables and extension leads are kept away from water, grease and heat sources."),
        new("PLUGS_SOCKETS_OK", "Plugs and sockets show no burn marks, cracks or looseness."),
        new("NO_DAISY_CHAINS", "No overloaded power strips or chained extension leads."),
        new("EQUIPMENT_GROUNDING", "Metal equipment frames are earthed (grounding conductor connected)."),
        new("PANEL_DRY", "No moisture, condensation or leaks near distribution boards."),
        new("EMERGENCY_LIGHTING", "Emergency lighting and exit signs work."),
        new("EXTINGUISHER_CLASS_C", "A CO2 / class C extinguisher is available and within its inspection date."),
        new("LOCKOUT_KIT", "Lockout padlocks and 'do not energize' tags are available."),
        new("STAFF_BRIEFED", "Shift staff know the emergency isolation procedure and where the main breaker is.")
    ];

    public static string? LabelOf(string code) => Items.FirstOrDefault(i => i.Code == code)?.Label;
}

public sealed record ChecklistItem(string Code, string Label, string Result, string? Notes);

/// <summary>
///     Weekly electrical network inspection of a store. Drafts can be revised; a submitted checklist is final.
///     Score = PASS / (PASS + FAIL) x 100 (items marked NOT_APPLICABLE do not count).
/// </summary>
public sealed class WeeklySafetyChecklist
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Id { get; private set; } = null!;
    public string StoreId { get; private set; } = null!;
    public string InspectorStaffId { get; private set; } = null!;
    public string InspectorName { get; private set; } = null!;
    /// <summary>Monday of the inspected ISO week.</summary>
    public DateOnly WeekStartDate { get; private set; }
    public string ItemsJson { get; private set; } = "[]";
    public double Score { get; private set; }
    public string Status { get; private set; } = ChecklistStatus.Draft;
    public string? Observations { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }
    public string CreatedByUserId { get; private set; } = null!;
    private WeeklySafetyChecklist() { }

    public IReadOnlyList<ChecklistItem> Items => JsonSerializer.Deserialize<List<ChecklistItem>>(ItemsJson, Json) ?? [];

    public static WeeklySafetyChecklist Start(string storeId, string inspectorStaffId, string inspectorName, DateOnly weekOf,
        IEnumerable<ChecklistItem> items, string? observations, bool submit, string actor, DateTime nowUtc)
    {
        var checklist = new WeeklySafetyChecklist
        {
            Id = "checklist-" + Guid.NewGuid().ToString("N"), StoreId = storeId, WeekStartDate = WeekStart(weekOf),
            CreatedAtUtc = nowUtc, CreatedByUserId = actor
        };
        checklist.Apply(inspectorStaffId, inspectorName, items, observations, submit, nowUtc);
        return checklist;
    }

    public void Revise(string inspectorStaffId, string inspectorName, IEnumerable<ChecklistItem> items, string? observations, bool submit, DateTime nowUtc)
    {
        if (Status == ChecklistStatus.Submitted) throw new ChecklistLockedException();
        Apply(inspectorStaffId, inspectorName, items, observations, submit, nowUtc);
    }

    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private void Apply(string inspectorStaffId, string inspectorName, IEnumerable<ChecklistItem> items, string? observations, bool submit, DateTime nowUtc)
    {
        InspectorStaffId = Text(inspectorStaffId, 60, "inspectorStaffId");
        InspectorName = Text(inspectorName, 120, "inspectorName");
        if (observations != null && observations.Trim().Length > 2000) throw new ArgumentException("observations must contain at most 2000 characters.");
        Observations = string.IsNullOrWhiteSpace(observations) ? null : observations.Trim();

        var normalized = new List<ChecklistItem>();
        foreach (var item in items ?? [])
        {
            var code = (item?.Code ?? "").Trim().ToUpperInvariant();
            var label = SafetyChecklistCatalog.LabelOf(code) ?? throw new ArgumentException($"Unknown checklist item '{item?.Code}'.");
            var result = (item!.Result ?? "").Trim().ToUpperInvariant();
            if (!ChecklistItemResult.All.Contains(result)) throw new ArgumentException($"Item {code}: result must be PASS, FAIL or NOT_APPLICABLE.");
            var notes = string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes.Trim();
            if (notes is { Length: > 500 }) throw new ArgumentException($"Item {code}: notes must contain at most 500 characters.");
            if (submit && result == ChecklistItemResult.Fail && notes == null)
                throw new ArgumentException($"Item {code}: a failed item needs notes describing the finding.");
            normalized.Add(new ChecklistItem(code, label, result, notes));
        }
        if (normalized.GroupBy(i => i.Code).Any(g => g.Count() > 1)) throw new ArgumentException("Each checklist item may appear only once.");
        if (submit)
        {
            var missing = SafetyChecklistCatalog.Items.Select(i => i.Code).Except(normalized.Select(i => i.Code)).ToList();
            if (missing.Count > 0) throw new ArgumentException("A submitted checklist must answer every item. Missing: " + string.Join(", ", missing) + ".");
        }

        var order = SafetyChecklistCatalog.Items.Select((d, i) => (d.Code, i)).ToDictionary(x => x.Code, x => x.i);
        normalized = normalized.OrderBy(i => order[i.Code]).ToList();
        var pass = normalized.Count(i => i.Result == ChecklistItemResult.Pass);
        var fail = normalized.Count(i => i.Result == ChecklistItemResult.Fail);
        ItemsJson = JsonSerializer.Serialize(normalized, Json);
        Score = pass + fail == 0 ? (normalized.Count > 0 ? 100 : 0) : Math.Round(pass * 100.0 / (pass + fail), 2);
        UpdatedAtUtc = nowUtc;
        if (submit) { Status = ChecklistStatus.Submitted; SubmittedAtUtc = nowUtc; }
    }

    private static string Text(string value, int max, string field)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0 || text.Length > max) throw new ArgumentException($"{field} is required and must contain at most {max} characters.");
        return text;
    }

    public string WeekLabel => ISOWeek.GetYear(WeekStartDate.ToDateTime(TimeOnly.MinValue)) + "-W"
        + ISOWeek.GetWeekOfYear(WeekStartDate.ToDateTime(TimeOnly.MinValue)).ToString("00", CultureInfo.InvariantCulture);
}

/// <summary>A submitted checklist cannot be changed.</summary>
public sealed class ChecklistLockedException() : Exception("The checklist was already submitted and can no longer be changed.");
