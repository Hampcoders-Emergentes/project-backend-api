using System.Reflection;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Processing.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Processing.Domain.Repositories;
using Hampcoders.Electrolink.API.Processing.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Handler = Hampcoders.Electrolink.API.Processing.Application.Internal.EventHandlers.ConsumptionThresholdsUpdatedEventHandler;

namespace Electrolink.Backend.Characterization.Tests.Processing;

internal sealed class ThresholdEventScenario
{
    internal const string Owner = "ho-m28-owner";
    internal const string OtherOwner = "ho-m28-other";
    internal const string OwnerDevice = "dev-m28-owner";
    internal const string SecondOwnerDevice = "dev-m28-second";
    internal const string UnrelatedDevice = "dev-m28-unrelated";
    internal const string DemoDevice = "dev-electrolink-esp32-01";
    internal Dictionary<string, StreamState> Persisted { get; private set; }

    internal ThresholdEventScenario(string demo, bool ownerStreams = true)
    {
        var streams = new List<DeviceReadingStream>();
        if (ownerStreams)
        {
            streams.Add(Create(OwnerDevice, Owner));
            streams.Add(Create(SecondOwnerDevice, Owner));
        }
        streams.Add(Create(UnrelatedDevice, OtherOwner));
        if (demo != "absent") streams.Add(Create(DemoDevice, demo == "same-owner" ? Owner : OtherOwner));
        Persisted = Snapshot(streams);
    }

    internal Delivery NewDelivery(string? failurePoint = null, Exception? failure = null) => new(this, failurePoint, failure);

    private static DeviceReadingStream Create(string id, string owner) => DeviceReadingStream.Create(
        DeviceId.From(id), PropertyId.From("prop-m28-" + (owner == Owner ? "owner" : "other")), ClientIdentity.FromOwnerId(owner));

    internal sealed record StreamState(string StreamId, string DeviceId, string PropertyId,
        ClientIdentity Owner, ThresholdConfig Thresholds, DateTime LastReceivedAt)
    {
        internal static StreamState Capture(DeviceReadingStream stream) => new(stream.StreamId.Value,
            stream.DeviceId.Value, stream.PropertyId.Value, stream.Owner with { }, stream.CustomThresholds with { }, stream.LastReceivedAt);

        internal DeviceReadingStream Restore()
        {
            // Fixtures are active, empty streams. Preserve stable IDs/timestamp across delivery scopes.
            var stream = DeviceReadingStream.Create(
                Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects.DeviceId.From(DeviceId),
                Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects.PropertyId.From(PropertyId),
                Owner with { }, Thresholds with { });
            typeof(DeviceReadingStream).GetProperty(nameof(DeviceReadingStream.StreamId))!
                .SetValue(stream, Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects.StreamId.From(StreamId));
            typeof(DeviceReadingStream).GetProperty(nameof(DeviceReadingStream.LastReceivedAt))!.SetValue(stream, LastReceivedAt);
            return stream;
        }
    }

    private static Dictionary<string, StreamState> Snapshot(IEnumerable<DeviceReadingStream> streams) =>
        streams.ToDictionary(stream => stream.DeviceId.Value, StreamState.Capture);

    internal sealed class Delivery : IUnitOfWork
    {
        private readonly ThresholdEventScenario store;
        private readonly Handler handler;
        private readonly string? failurePoint;
        private readonly Exception? failure;
        private readonly HashSet<string> pending = new(StringComparer.Ordinal);
        internal Dictionary<string, DeviceReadingStream> Memory { get; }
        internal List<UpdateCustomThresholdsCommand> Commands { get; } = new();
        internal List<string> Updates { get; } = new();
        internal List<string> Trace { get; } = new();
        internal List<Dictionary<string, StreamState>> SaveAttempts { get; } = new();
        internal List<Dictionary<string, StreamState>> ConfirmedSnapshots { get; } = new();
        internal int ConfirmedSaves => ConfirmedSnapshots.Count;
        // Counts differing threshold values committed by this double, not SQL row counts/audit effects.
        internal int EffectiveWrites { get; private set; }

        internal Delivery(ThresholdEventScenario store, string? failurePoint, Exception? failure)
        {
            this.store = store;
            this.failurePoint = failurePoint;
            this.failure = failure;
            Memory = store.Persisted.ToDictionary(pair => pair.Key, pair => pair.Value.Restore());
            var repository = Proxy<IDeviceReadingStreamRepository>((method, args) =>
            {
                if (method.Name == nameof(IDeviceReadingStreamRepository.FindByOwnerAsync) && args is [ClientIdentity owner])
                {
                    Step("lookup-owner");
                    return Task.FromResult<IEnumerable<DeviceReadingStream>>(Memory.Values.Where(stream => stream.Owner == owner).ToArray());
                }
                if (method.Name == nameof(IDeviceReadingStreamRepository.FindByDeviceIdAsync) && args is [string id])
                {
                    Step("lookup-demo");
                    if (id != DemoDevice) throw new InvalidOperationException("Unexpected device lookup.");
                    return Task.FromResult(Memory.GetValueOrDefault(id));
                }
                if (method.Name == nameof(IDeviceReadingStreamRepository.UpdateAsync) && args is [DeviceReadingStream stream])
                {
                    Updates.Add(stream.DeviceId.Value);
                    Step(UpdateLabel(stream.DeviceId.Value));
                    pending.Add(stream.DeviceId.Value);
                    return Task.CompletedTask;
                }
                throw new InvalidOperationException("Unexpected repository access: " + method.Name);
            });
            var service = new DeviceReadingStreamCommandService(repository,
                Proxy<IAnomalyCommandService>(Unexpected), this, Proxy<IMediator>(Unexpected),
                NullLogger<DeviceReadingStreamCommandService>.Instance);
            var recordingService = Proxy<IDeviceReadingStreamCommandService>((method, args) =>
            {
                if (method.Name != "Handle" || args is not [UpdateCustomThresholdsCommand command])
                    throw new InvalidOperationException("Unexpected command service access.");
                Commands.Add(command);
                Step("command");
                // Delegate to the real service; do not implement its threshold/owner algorithm here.
                return service.Handle(command);
            });
            handler = new Handler(recordingService, repository, this, NullLogger<Handler>.Instance);
        }

        internal Task Handle(ConsumptionThresholdsUpdatedIntegrationEvent evt) => handler.Handle(evt, CancellationToken.None);

        public Task CompleteAsync()
        {
            SaveAttempts.Add(Snapshot(Memory.Values));
            Step("save:" + SaveAttempts.Count);
            var committed = new Dictionary<string, StreamState>(store.Persisted, StringComparer.Ordinal);
            foreach (var id in pending)
            {
                var state = StreamState.Capture(Memory[id]);
                if (committed[id].Thresholds != state.Thresholds) EffectiveWrites++;
                committed[id] = state;
            }
            store.Persisted = committed;
            pending.Clear();
            ConfirmedSnapshots.Add(new Dictionary<string, StreamState>(committed, StringComparer.Ordinal));
            return Task.CompletedTask;
        }

        private void Step(string point)
        {
            Trace.Add(point);
            if (point == failurePoint && (point != "update-demo" || ConfirmedSaves == 1)) throw failure ?? new InvalidOperationException("Synthetic failure.");
        }

        // Expected trace for these fixed fixtures; not used to perform any handler effect.
        internal string[] ExpectedSuccessfulTrace()
        {
            var expected = new List<string> { "command", "lookup-owner" };
            expected.AddRange(Memory.Values.Where(stream => stream.Owner.ClientId == Owner)
                .Select(stream => UpdateLabel(stream.DeviceId.Value)));
            expected.AddRange(new[] { "save:1", "lookup-demo" });
            if (Memory.TryGetValue(DemoDevice, out var demo) && demo.Owner == ClientIdentity.FromOwnerId(Owner)) expected.AddRange(new[] { "update-demo", "save:2" });
            return expected.ToArray();
        }

        private static string UpdateLabel(string id) => id switch
        {
            OwnerDevice => "update-owner", SecondOwnerDevice => "update-second-owner", DemoDevice => "update-demo",
            _ => throw new InvalidOperationException("Unexpected stream update: " + id)
        };
    }

    private static object? Unexpected(MethodInfo method, object?[]? args) =>
        throw new InvalidOperationException("Unexpected dependency call: " + method.Name);

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, ThresholdDependencyDouble>();
        ((ThresholdDependencyDouble)(object)proxy).Call = call;
        return proxy;
    }
}

// Public/non-sealed solely for DispatchProxy's generated subclass.
public class ThresholdDependencyDouble : DispatchProxy
{
    internal Func<MethodInfo, object?[]?, object?> Call { get; set; } =
        (_, _) => throw new InvalidOperationException("Unconfigured dependency.");
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        Call(targetMethod ?? throw new InvalidOperationException("Missing dependency method."), args);
}
