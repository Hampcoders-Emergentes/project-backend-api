using System.Text.Json;

namespace Electrolink.DeviceIdReconciliation;

public static class Reconciler
{
    private static readonly HashSet<string> Sources = new(StringComparer.Ordinal)
        { "processingStream", "processingRelayCommand", "edgeHistoryRequest" };
    private static readonly HashSet<string> DemoProperties = new(StringComparer.Ordinal)
        { "prop-demo", "prop-edge-default" };

    public static Report Reconcile(Snapshot snapshot)
    {
        Validate(snapshot);
        var findings = new List<Finding>();
        var collisionKeys = snapshot.Assets.SelectMany(a => new[] { (Key: a.InternalId, Target: a.InternalId), (Key: a.SerialNumber, Target: a.InternalId) })
            .GroupBy(link => link.Key, StringComparer.Ordinal).Where(group => group.Select(link => link.Target).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        AddFindings(findings, collisionKeys, "IdentifierCollision", "assets");
        var duplicateIds = Duplicates(snapshot.Assets.Select(a => a.InternalId));
        var duplicateSerials = Duplicates(snapshot.Assets.Select(a => a.SerialNumber));
        var duplicateAssetRecords = Duplicates(snapshot.Assets.Select(a => a.RecordId));
        var duplicateObservationRecords = Duplicates(snapshot.Observations.Select(ObservationKey));
        var duplicateMappingRecords = Duplicates(snapshot.Mappings.Select(m => m.RecordId));
        AddFindings(findings, duplicateIds, "DuplicateInternalId", "assets");
        AddFindings(findings, duplicateSerials, "DuplicateSerialNumber", "assets");
        AddFindings(findings, duplicateAssetRecords, "DuplicateRecordId", "assets");
        AddFindings(findings, duplicateObservationRecords, "DuplicateRecordId", "observations");
        AddFindings(findings, duplicateMappingRecords, "DuplicateRecordId", "mappings");
        foreach (var map in snapshot.Mappings)
            if (!snapshot.Assets.Any(a => a.InternalId == map.InternalId))
                findings.Add(new Finding("UnknownMappingTarget", "mappings", map.RecordId));

        var identifiers = snapshot.Assets.Select(a => a.InternalId)
            .Concat(snapshot.Observations.Select(o => o.DeviceId))
            .Concat(snapshot.Mappings.Select(m => m.ObservedId)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var rows = new List<ReconciledId>();
        foreach (var id in identifiers)
        {
            var maps = Ordered(snapshot.Mappings.Where(m => m.ObservedId == id));
            var observations = Ordered(snapshot.Observations.Where(o => o.DeviceId == id));
            // The inventory explicitly links each exact serial to its internal ID. No normalization.
            var targets = snapshot.Assets.Where(a => a.InternalId == id || a.SerialNumber == id).Select(a => a.InternalId)
                .Concat(maps.Select(m => m.InternalId)).ToHashSet(StringComparer.Ordinal);
            var assets = Ordered(snapshot.Assets.Where(a => targets.Contains(a.InternalId)));
            var issues = new HashSet<string>(StringComparer.Ordinal);
            if (assets.Length == 0) issues.Add("NoInventoryMatch");
            if (targets.Count > 1 || assets.Any(a => duplicateIds.Contains(a.InternalId) || duplicateSerials.Contains(a.SerialNumber) || collisionKeys.Contains(a.InternalId) || collisionKeys.Contains(a.SerialNumber)))
                issues.Add("ConflictingInventoryIdentity");
            if (assets.Any(a => duplicateAssetRecords.Contains(a.RecordId))
                || observations.Any(o => duplicateObservationRecords.Contains(ObservationKey(o)))
                || maps.Any(m => duplicateMappingRecords.Contains(m.RecordId))) issues.Add("DuplicateRecordId");
            if (maps.Any(m => !snapshot.Assets.Any(a => a.InternalId == m.InternalId))) issues.Add("UnknownMappingTarget");
            if (observations.Any(o => o.PropertyId != null && assets.Any(a => a.PropertyId != null && a.PropertyId != o.PropertyId)))
                issues.Add("PropertyConflict");
            if (observations.Any(o => o.PropertyId != null) && assets.Any(a => a.PropertyId == null)) issues.Add("PropertyUnverified");
            if (assets.Any(a => a.IsDemo || (a.PropertyId != null && DemoProperties.Contains(a.PropertyId)))
                || observations.Any(o => o.IsDemo || (o.PropertyId != null && DemoProperties.Contains(o.PropertyId)))
                || id == "dev-electrolink-esp32-01") issues.Add("DemoReference");
            var hasCommands = observations.Any(o => o.Source == "processingRelayCommand");
            if (!hasCommands) issues.Add("NoRelayCommandObservation");
            if (!observations.Any(o => o.Source == "edgeHistoryRequest")) issues.Add("NoEdgeObservation");
            if (assets.Length > 0 && observations.Length == 0) issues.Add("InventoryOnly");
            var classification = issues.Contains("ConflictingInventoryIdentity") || issues.Contains("PropertyConflict") ? "Ambiguous"
                : issues.Contains("DemoReference") ? "DemoReview"
                : assets.Length == 0 ? "Unmatched"
                : issues.Contains("DuplicateRecordId") || issues.Contains("UnknownMappingTarget") || issues.Contains("PropertyUnverified") ? "Review"
                : "Supported";
            if (classification == "Ambiguous") findings.Add(new Finding("AmbiguousAssociation", "identifiers", id));
            rows.Add(new ReconciledId(id, classification, assets, observations, maps, hasCommands, issues.Order(StringComparer.Ordinal).ToArray()));
        }
        return new Report(1, snapshot.SnapshotId, snapshot.Environment, rows.ToArray(), Ordered(findings));
    }

    private static string ObservationKey(ObservationRow row) => row.Source + ":" + row.RecordId;
    private static HashSet<string> Duplicates(IEnumerable<string> values) => values.GroupBy(x => x, StringComparer.Ordinal)
        .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
    private static T[] Ordered<T>(IEnumerable<T> values) => values.OrderBy(value => JsonSerializer.Serialize(value), StringComparer.Ordinal).ToArray();
    private static void AddFindings(List<Finding> target, IEnumerable<string> keys, string code, string source)
    {
        foreach (var key in keys) target.Add(new Finding(code, source, key));
    }

    private static void Validate(Snapshot s)
    {
        static bool Text(string? value) => !string.IsNullOrWhiteSpace(value);
        if (s == null || s.SchemaVersion != 1 || !Text(s.SnapshotId) || !Text(s.Environment)
            || s.Assets == null || s.Observations == null || s.Mappings == null)
            throw new InvalidDataException("Invalid snapshot structure.");
        if (s.Assets.Any(a => a == null || !Text(a.RecordId) || !Text(a.InternalId) || !Text(a.SerialNumber)
                || !Text(a.Status) || !Text(a.EvidenceRef) || (a.PropertyId != null && !Text(a.PropertyId)))
            || s.Observations.Any(o => o == null || !Text(o.RecordId) || !Sources.Contains(o.Source ?? "")
                || !Text(o.DeviceId) || !Text(o.EvidenceRef) || (o.PropertyId != null && !Text(o.PropertyId)))
            || s.Mappings.Any(m => m == null || !Text(m.RecordId) || !Text(m.ObservedId) || !Text(m.InternalId) || !Text(m.EvidenceRef)))
            throw new InvalidDataException("Missing required evidence or unsupported source.");
    }
}
