using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.AlertManagement;

public sealed class AlertTests
{
    [Theory]
    [InlineData("Critical", "CRITICAL")]
    [InlineData("High", "NON_CRITICAL")]
    [InlineData("Medium", "NON_CRITICAL")]
    [InlineData("Low", "NON_CRITICAL")]
    public void Original_severity_is_preserved_and_ack_is_not_resolution(string original, string mapped)
    {
        var actor = UserId.From("us-m34-synthetic"); var now = DateTime.UtcNow;
        var a = Alert.Open(new("binding-synthetic", "reading-synthetic", "equipment-synthetic", actor,
            "VoltageSpike", original, now.AddSeconds(-1), now));
        Assert.Equal("OPEN", a.Status); Assert.Equal(mapped, a.Severity); Assert.Equal(original, a.DetectionSeverity);
        a.Acknowledge(actor, now); var date = a.AcknowledgedAtUtc;
        a.Acknowledge(actor, now.AddSeconds(5));
        Assert.Equal("ACKNOWLEDGED", a.Status); Assert.Equal(date, a.AcknowledgedAtUtc); Assert.Equal(actor.Value, a.AcknowledgedByUserId);
        Assert.Equal("VoltageSpike", a.AnomalyType);
    }
}
