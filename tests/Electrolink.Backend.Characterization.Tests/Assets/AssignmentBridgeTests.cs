using Hampcoders.Electrolink.API.Assets.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.Assets;

public sealed class AssignmentBridgeTests
{
    [Fact]
    public async Task Empty_selection_is_visible_failure_without_stock_fallback()
    {
        var calls = 0;
        var commands = AssignmentHttpFixture.Proxy<IIoTDeviceCommandService>((m, a) => { calls++; return Task.FromResult<IoTDevice>(null!); });
        var handler = new IoTInstallationServiceScheduledEventHandler(commands, NullLogger<IoTInstallationServiceScheduledEventHandler>.Instance);
        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new("prop-synthetic", "req-synthetic", "", DateTime.UtcNow), default));
        Assert.Equal(0, calls);
    }
    [Fact]
    public async Task Explicit_selection_is_forwarded_without_choosing_first_stock()
    {
        string? selected = null;
        var commands = AssignmentHttpFixture.Proxy<IIoTDeviceCommandService>((m, a) =>
        {
            var command = Assert.IsType<AssignDeviceToPropertyCommand>(a![0]); selected = command.DeviceId;
            Assert.Equal("prop-synthetic", command.PropertyId); Assert.Equal("req-synthetic", command.InstallationRequestId);
            return Task.FromResult<IoTDevice>(null!);
        });
        var handler = new IoTInstallationServiceScheduledEventHandler(commands, NullLogger<IoTInstallationServiceScheduledEventHandler>.Instance);
        await handler.Handle(new("prop-synthetic", "req-synthetic", "dev-chosen", DateTime.UtcNow), default);
        Assert.Equal("dev-chosen", selected);
    }
}
