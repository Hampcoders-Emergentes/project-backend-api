using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Xunit;

namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

public sealed class EquipmentTimestampTests
{
    [Fact]
    public void Capture_uses_postgresql_microseconds_so_replay_retains_identity_after_storage()
    {
        var binding = EquipmentDeviceBinding.Link("equipment-00000000000000000000000000000001",
            UserId.From("us-m33-synthetic"), "dev-m33", "SERIAL-M33", "SERIAL-M33", "prop-m33", "prof-m33");
        var microsecond = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234560);
        EquipmentMeasurement Capture(DateTime time, float voltage = 220) => EquipmentMeasurement.Capture(
            binding, "m33-precision", time, voltage, 1, 1, 60, "EDGE", microsecond.AddMinutes(1));
        var fromWire = Capture(microsecond.AddTicks(7));
        var fromPostgres = Capture(microsecond);
        Assert.Equal(microsecond, fromWire.CapturedAtUtc);
        Assert.True(fromPostgres.SamePayload(fromWire));
        Assert.False(fromPostgres.SamePayload(Capture(microsecond.AddTicks(10))));
        Assert.False(fromPostgres.SamePayload(Capture(microsecond.AddTicks(7), voltage: 300)));
    }
}
