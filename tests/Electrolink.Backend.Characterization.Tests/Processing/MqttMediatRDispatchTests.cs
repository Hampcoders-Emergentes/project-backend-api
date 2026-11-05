using Electrolink.Backend.Characterization.Tests.Assets;
using Hampcoders.Electrolink.API.Processing.Application.Internal.CommandHandlers;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Processing.Domain.Services;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Electrolink.Backend.Characterization.Tests.Processing;

public sealed class MqttMediatRDispatchTests
{
    [Fact]
    public async Task Every_mqtt_command_reaches_its_scoped_service_through_mediatr()
    {
        var handled = new List<object>();
        var streams = AssignmentHttpFixture.Proxy<IDeviceReadingStreamCommandService>((method, args) =>
        {
            handled.Add(args![0]!);
            return args[0] is IngestDeviceReadingCommand ? Task.FromResult(true) : Task.CompletedTask;
        });
        var relays = AssignmentHttpFixture.Proxy<IRelayCommandService>((method, args) =>
        {
            handled.Add(args![0]!);
            return Task.CompletedTask;
        });

        var services = new ServiceCollection();
        services.AddMediatR(typeof(MqttIngestionCommandHandler).Assembly);
        // Scoped like production: ValidateScopes fails if a handler is resolved outside a message scope.
        services.AddScoped(_ => streams);
        services.AddScoped(_ => relays);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var ingest = new IngestDeviceReadingCommand("dev-1", "read-1", DateTime.UtcNow, 220f, 1f, 1f, 60f, "Edge");
        var anomaly = new ReportEdgeAnomalyCommand("dev-1", "Overload", "read-1", "{}");
        var disconnected = new MarkDeviceAsDisconnectedCommand("dev-1");
        var relayAck = new AcknowledgeRelayExecutionCommand("cmd-1", "dev-1", true, DateTime.UtcNow);

        Assert.True(await mediator.Send(ingest));
        await mediator.Send(anomaly);
        await mediator.Send(disconnected);
        await mediator.Send(relayAck);

        Assert.Equal(new object[] { ingest, anomaly, disconnected, relayAck }, handled);
    }
}
