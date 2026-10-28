using Hampcoders.Electrolink.API.Assets.Domain.Model.Queries;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.Assets;

public sealed class DeviceReadPolicyTests
{
    [Theory]
    [InlineData("HomeOwner")]
    [InlineData("Company")]
    public async Task Eligible_inventory_owner_reads_without_mutation(string role)
    {
        var f = new DeviceRetirementPolicyTests.Fixture(role, "valid");
        var before = f.Device.Status;
        Assert.Same(f.Device, await f.Queries.Handle(new GetOwnedIoTDeviceByIdQuery(f.Actor, f.Device.Id.Value)));
        Assert.Equal(before, f.Device.Status);
        Assert.Equal(0, f.Saves); Assert.Equal(0, f.Events);
    }

    [Theory]
    [InlineData("inactive-profile")]
    [InlineData("unassigned")]
    [InlineData("foreign")]
    public async Task Missing_authority_returns_no_inventory(string condition)
    {
        var f = new DeviceRetirementPolicyTests.Fixture("HomeOwner", condition);
        var before = f.Device.Status;
        Assert.Null(await f.Queries.Handle(new GetOwnedIoTDeviceByIdQuery(f.Actor, f.Device.Id.Value)));
        Assert.Equal(before, f.Device.Status);
        Assert.Equal(0, f.Saves); Assert.Equal(0, f.Events);
    }

    [Fact]
    public async Task Retirement_keeps_read_access_for_eligible_owner()
    {
        var f = new DeviceRetirementPolicyTests.Fixture("HomeOwner", "valid");
        f.Device.Decommission("Synthetic"); f.Device.ClearDomainEvents();
        Assert.Same(f.Device, await f.Queries.Handle(new GetOwnedIoTDeviceByIdQuery(f.Actor, f.Device.Id.Value)));
        Assert.Equal(0, f.Saves); Assert.Equal(0, f.Events);
    }
}
