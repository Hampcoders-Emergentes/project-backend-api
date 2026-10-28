using System.Text.Json;
using Electrolink.DeviceIdReconciliation;
using Xunit;

namespace Electrolink.Backend.Characterization.Tests.Processing;

public sealed class ReconciliationTests
{
    private static InventoryRow A => new("asset-a", "dev-a", "serial-a", "prop-a", "Installed", "inventory:a");
    private static InventoryRow B => new("asset-b", "dev-b", "serial-b", "prop-b", "Installed", "inventory:b");
    private static Snapshot Input(InventoryRow[]? assets = null, ObservationRow[]? observations = null, MappingRow[]? mappings = null) =>
        new(1, "snapshot-synthetic", "synthetic", assets ?? new[] { A, B }, observations ?? Array.Empty<ObservationRow>(), mappings ?? Array.Empty<MappingRow>());
    private static ObservationRow Edge(string id) => new("edge-" + id, "edgeHistoryRequest", id, null, "edge-export:" + id);
    private static ReconciledId Row(Report report, string id) => Assert.Single(report.Identifiers, row => row.DeviceId == id);

    [Fact]
    public void InternalAndSerialMatchesKeepExactObservedIdsAndEvidence()
    {
        var report = Reconciler.Reconcile(Input(observations: new[] { Edge("dev-a"), Edge("serial-b") }));
        Assert.Equal("Supported", Row(report, "dev-a").Classification);
        var bySerial = Row(report, "serial-b");
        Assert.Equal("Supported", bySerial.Classification);
        Assert.Equal("serial-b", bySerial.DeviceId);
        Assert.Equal("dev-b", Assert.Single(bySerial.MatchedAssets).InternalId);
        Assert.Equal("inventory:b", Assert.Single(bySerial.MatchedAssets).EvidenceRef);
        Assert.Equal("edge-export:serial-b", Assert.Single(bySerial.Observations).EvidenceRef);
        Assert.False(report.GrantsAccess);
    }

    [Theory]
    [InlineData("DEV-A")]
    [InlineData(" dev-a")]
    [InlineData("dev-a ")]
    [InlineData("dev-a-similar")]
    public void NoNormalizationOrPrefixGuessing(string id) =>
        Assert.Equal("Unmatched", Row(Reconciler.Reconcile(Input(observations: new[] { Edge(id) })), id).Classification);

    [Fact]
    public void ExplicitMappingNeedsExistingInventoryAndKeepsItsProvenance()
    {
        var mapping = new MappingRow("mapping-a", "edge-legacy-a", "dev-a", "review:physical-label-a");
        var row = Row(Reconciler.Reconcile(Input(observations: new[] { Edge("edge-legacy-a") }, mappings: new[] { mapping })), "edge-legacy-a");
        Assert.Equal("Supported", row.Classification);
        Assert.Equal("mapping-a", Assert.Single(row.Mappings).RecordId);
        Assert.Equal("edge-legacy-a", row.DeviceId);
    }

    [Fact]
    public void UnknownMappingTargetStaysPendingInsteadOfInventingDevice()
    {
        var report = Reconciler.Reconcile(Input(mappings: new[] { new MappingRow("map", "edge-x", "dev-absent", "review:unverified") }));
        Assert.Equal("Unmatched", Row(report, "edge-x").Classification);
        Assert.Contains("UnknownMappingTarget", Row(report, "edge-x").Issues);
        Assert.Contains(report.Findings, finding => finding.Code == "UnknownMappingTarget");
    }

    [Fact]
    public void ContradictoryDirectAndExplicitMatchesRemainAmbiguous()
    {
        var row = Row(Reconciler.Reconcile(Input(mappings: new[] { new MappingRow("map", "dev-a", "dev-b", "review:contradiction") })), "dev-a");
        Assert.Equal("Ambiguous", row.Classification);
        Assert.Equal(2, row.MatchedAssets.Length);
    }

    [Fact]
    public void DuplicateInventoryIsVisibleEvenWhenRowsAreIdentical()
    {
        var report = Reconciler.Reconcile(Input(assets: new[] { A, A }));
        Assert.Equal("Ambiguous", Row(report, "dev-a").Classification);
        Assert.Equal(2, Row(report, "dev-a").MatchedAssets.Length);
        Assert.Contains(report.Findings, finding => finding.Code == "DuplicateInternalId");
        Assert.Contains(report.Findings, finding => finding.Code == "DuplicateRecordId");
    }

    [Fact]
    public void SerialCollisionAndIdSerialCollisionDoNotPickFirstRow()
    {
        var collided = B with { SerialNumber = A.InternalId };
        var report = Reconciler.Reconcile(Input(assets: new[] { A, collided }, observations: new[] { Edge("dev-a") }));
        Assert.Equal("Ambiguous", Row(report, "dev-a").Classification);
        Assert.Equal("Ambiguous", Row(report, "dev-b").Classification);
        Assert.Contains(report.Findings, finding => finding.Code == "IdentifierCollision");
        var serialCollision = Reconciler.Reconcile(Input(assets: new[] { A, B with { SerialNumber = A.SerialNumber } }));
        Assert.Equal("Ambiguous", Row(serialCollision, "dev-a").Classification);
        Assert.Contains(serialCollision.Findings, finding => finding.Code == "DuplicateSerialNumber");
    }

    [Fact]
    public void DuplicateObservationReferenceRemainsReviewable()
    {
        var edge = Edge("dev-a");
        var row = Row(Reconciler.Reconcile(Input(observations: new[] { edge, edge })), "dev-a");
        Assert.Equal("Review", row.Classification);
        Assert.Equal(2, row.Observations.Length);
        Assert.Contains("DuplicateRecordId", row.Issues);
    }

    [Theory]
    [InlineData("prop-demo", false)]
    [InlineData("prop-edge-default", false)]
    [InlineData("prop-a", true)]
    public void DemoReferencesAreReviewNotAuthority(string property, bool explicitDemo)
    {
        var report = Reconciler.Reconcile(Input(assets: new[] { A with { PropertyId = property, IsDemo = explicitDemo } }));
        Assert.Equal("DemoReview", Row(report, "dev-a").Classification);
    }

    [Fact]
    public void DemoObservationCannotBePromotedByExplicitMapping()
    {
        var obs = Edge("legacy-demo") with { IsDemo = true };
        var row = Row(Reconciler.Reconcile(Input(observations: new[] { obs }, mappings: new[] { new MappingRow("map", obs.DeviceId, A.InternalId, "review:demo") })), obs.DeviceId);
        Assert.Equal("DemoReview", row.Classification);
    }

    [Fact]
    public void KnownEdgeFallbackIsFlaggedEvenWithoutInventoryMatch()
    {
        var row = Row(Reconciler.Reconcile(Input(observations: new[] { Edge("dev-electrolink-esp32-01") })), "dev-electrolink-esp32-01");
        Assert.Equal("DemoReview", row.Classification);
        Assert.Contains("NoInventoryMatch", row.Issues);
    }

    [Fact]
    public void PropertyContradictionIsNotResolvedUsingDemoOrFirstMatch()
    {
        var observation = new ObservationRow("stream-a", "processingStream", A.InternalId, "prop-other", "processing:stream-a");
        Assert.Equal("Ambiguous", Row(Reconciler.Reconcile(Input(observations: new[] { observation })), A.InternalId).Classification);
    }

    [Fact]
    public void InventoryDeviceWithoutCommandsIsSupportedButNeverApproved()
    {
        var report = Reconciler.Reconcile(Input(observations: new[] { Edge(A.InternalId) }));
        var row = Row(report, A.InternalId);
        Assert.Equal("Supported", row.Classification);
        Assert.False(row.HasRelayCommandObservation);
        Assert.Contains("NoRelayCommandObservation", row.Issues);
        Assert.Equal("Supported", Row(report, B.InternalId).Classification);
        Assert.Contains("InventoryOnly", Row(report, B.InternalId).Issues);
        Assert.False(report.GrantsAccess);
    }

    [Fact]
    public void RelayEvidenceIsTrackedByExactHistoryId()
    {
        var command = new ObservationRow("cmd-a", "processingRelayCommand", A.InternalId, A.PropertyId, "processing:command-a");
        var report = Reconciler.Reconcile(Input(observations: new[] { command, Edge(A.SerialNumber) }));
        Assert.True(Row(report, A.InternalId).HasRelayCommandObservation);
        Assert.False(Row(report, A.SerialNumber).HasRelayCommandObservation);
    }

    [Fact]
    public void ResultsAreDeterministicAndIndependentOfArrayOrder()
    {
        var a = Input(observations: new[] { Edge(A.InternalId), Edge(B.SerialNumber) });
        var b = a with { Assets = a.Assets.Reverse().ToArray(), Observations = a.Observations.Reverse().ToArray() };
        Assert.Equal(JsonSerializer.Serialize(Reconciler.Reconcile(a)), JsonSerializer.Serialize(Reconciler.Reconcile(b)));
    }

    [Fact]
    public void MissingEvidenceIsRejectedRatherThanInvented()
    {
        Assert.Throws<InvalidDataException>(() => Reconciler.Reconcile(Input(assets: new[] { A with { EvidenceRef = "" } })));
    }
}
