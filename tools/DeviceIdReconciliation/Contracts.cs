namespace Electrolink.DeviceIdReconciliation;

public sealed record Snapshot(int SchemaVersion, string SnapshotId, string Environment,
    InventoryRow[] Assets, ObservationRow[] Observations, MappingRow[] Mappings);

public sealed record InventoryRow(string RecordId, string InternalId, string SerialNumber,
    string? PropertyId, string Status, string EvidenceRef, bool IsDemo = false);

public sealed record ObservationRow(string RecordId, string Source, string DeviceId,
    string? PropertyId, string EvidenceRef, bool IsDemo = false);

public sealed record MappingRow(string RecordId, string ObservedId, string InternalId, string EvidenceRef);

public sealed record Finding(string Code, string Source, string RecordKey);

public sealed record ReconciledId(string DeviceId, string Classification, InventoryRow[] MatchedAssets,
    ObservationRow[] Observations, MappingRow[] Mappings, bool HasRelayCommandObservation, string[] Issues);

public sealed record Report(int SchemaVersion, string SnapshotId, string Environment,
    ReconciledId[] Identifiers, Finding[] Findings, string? InputSha256 = null)
{
    // This report never acts as an authorization artifact.
    public bool GrantsAccess => false;
}
