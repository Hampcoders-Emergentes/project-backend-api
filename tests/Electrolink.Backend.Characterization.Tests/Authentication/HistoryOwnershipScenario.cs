using System.Reflection;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Processing.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Processing.Domain.Repositories;
using Hampcoders.Electrolink.API.Processing.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Electrolink.Backend.Characterization.Tests.Authentication;

// Two devices/properties/owners using existing domain relationships. No gateway is invented.
internal sealed class HistoryOwnershipScenario
{
    internal const string DeviceCredentialA = "synthetic-m22-device-credential-a";
    internal const string DeviceCredentialB = "synthetic-m22-device-credential-b";
    private static readonly ApiKeyHash HashA = ApiKeyHash.CreateFromPlainText(DeviceCredentialA);
    private static readonly ApiKeyHash HashB = ApiKeyHash.CreateFromPlainText(DeviceCredentialB);
    internal Property PropertyA { get; } = CreateProperty("ho-m22-a");
    internal Property PropertyB { get; } = CreateProperty("ho-m22-b");
    internal IoTDevice DeviceA { get; }
    internal IoTDevice DeviceB { get; }
    internal DeviceReadingStream StreamA { get; }
    internal DeviceReadingStream StreamB { get; }
    internal RelayControlCommand CommandA { get; }
    internal RelayControlCommand CommandB { get; }
    internal RelayHistoryRepositoryDouble Repository { get; }
    internal IIoTMonitoringQueryService Queries { get; }

    internal HistoryOwnershipScenario(bool withCommands = true)
    {
        DeviceA = CreateDevice("dev-serial-m22-a", HashA, PropertyA);
        DeviceB = CreateDevice("dev-serial-m22-b", HashB, PropertyB);
        StreamA = DeviceReadingStream.Create(DeviceId.From(DeviceA.Id.Value), PropertyA.Id, PropertyA.Owner);
        StreamB = DeviceReadingStream.Create(DeviceId.From(DeviceB.Id.Value), PropertyB.Id, PropertyB.Owner);
        CommandA = RelayControlCommand.IssueBySystem(StreamA.DeviceId, PropertyA.Id, ERelayTarget.Open);
        CommandB = RelayControlCommand.IssueBySystem(StreamB.DeviceId, PropertyB.Id, ERelayTarget.Closed);
        Repository = new RelayHistoryRepositoryDouble(withCommands ? new[] { CommandA, CommandB } : Array.Empty<RelayControlCommand>());
        // Real query service, only persistence is doubled. Unexpected cross-repository access fails.
        Queries = new IoTMonitoringQueryService(
            DispatchProxy.Create<IDeviceReadingStreamRepository, UnexpectedOwnershipDependency>(),
            DispatchProxy.Create<IAnomalyRecordRepository, UnexpectedOwnershipDependency>(), Repository);
    }

    private static Property CreateProperty(string owner) => Property.Create(
        ClientIdentity.FromHomeowner(owner), Address.Create("Synthetic street", "1", "Test district", "Test city", "Test country", "00000"),
        Geolocation.Create(0, 0, null, "SYNTHETIC"));

    private static IoTDevice CreateDevice(string serial, ApiKeyHash hash, Property property)
    {
        var device = IoTDevice.Register(SerialNumber.From(serial), hash, "synthetic-firmware");
        device.AssignToProperty(property.Id, InstallationRequestId.From("installation-" + serial));
        return device;
    }

    internal sealed class RelayHistoryRepositoryDouble(IEnumerable<RelayControlCommand> commands) : IRelayControlCommandRepository
    {
        internal List<string> RequestedDevices { get; } = new();
        public Task<IEnumerable<RelayControlCommand>> FindHistoryByDeviceAsync(string deviceId)
        {
            RequestedDevices.Add(deviceId);
            // Mirrors the existing repository's device-ID selection, not a new authorization policy.
            return Task.FromResult(commands.Where(command => command.DeviceId == DeviceId.From(deviceId)).OrderByDescending(command => command.IssuedAt).AsEnumerable());
        }
        public Task<RelayControlCommand?> FindPendingByDeviceAsync(string deviceId) => throw new InvalidOperationException("Unexpected pending query");
        public Task<RelayControlCommand?> FindByCommandIdAsync(string commandId) => throw new InvalidOperationException("Unexpected command query");
        public Task UpdateAsync(RelayControlCommand command) => throw new InvalidOperationException("Unexpected write");
        public Task AddAsync(RelayControlCommand entity) => throw new InvalidOperationException("Unexpected write");
        public Task<RelayControlCommand?> FindByIdAsync(RelayCommandId id) => throw new InvalidOperationException("Unexpected command query");
        public void Update(RelayControlCommand entity) => throw new InvalidOperationException("Unexpected write");
        public void Remove(RelayControlCommand entity) => throw new InvalidOperationException("Unexpected delete");
        public Task<IEnumerable<RelayControlCommand>> ListAsync() => throw new InvalidOperationException("Unexpected global listing");
    }
}

// Public and non-sealed only because DispatchProxy generates a derived test double at runtime.
public class UnexpectedOwnershipDependency : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        throw new InvalidOperationException("Unexpected dependency access: " + targetMethod?.Name);
}
