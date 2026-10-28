using Hampcoders.Electrolink.API.AlertManagement.Domain;
using System.Reflection;
using Hampcoders.Electrolink.API.Processing.Domain.Repositories;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Repositories;
using Hampcoders.Electrolink.API.Processing.Domain.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

internal sealed class EquipmentTelemetryScenario
{
    internal MemoryAlertRepository Alerts { get; } = new();
    internal IoTDevice Device { get; }
    internal IoTDevice ForeignDevice { get; }
    internal bool ProfileActive { get; set; } = true;
    internal bool MachineScope { get; set; } = true;
    internal bool DuplicateAlias { get; set; }
    internal List<EquipmentDeviceBinding> Bindings { get; } = new();
    internal List<EquipmentMeasurement> Measurements { get; } = new();
    internal Property Property { get; }
    private string ownerUserId = "";
    internal EquipmentTelemetryScenario(string deviceKey = "synthetic-m32-device-key")
    {
        Property = Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates.Property.Create(ClientIdentity.FromHomeowner("ho-m32-owner"),
            Address.Create("Synthetic street", "1", "Test district", "Test city", "Test country", "00000"),
            Geolocation.Create(0, 0, null, "SYNTHETIC"));
        Device = CreateDevice("SERIAL-M32-01", Property.Id, deviceKey);
        ForeignDevice = CreateDevice("SERIAL-M32-02", PropertyId.From("prop-m32-foreign"), deviceKey);
    }
    private static IoTDevice CreateDevice(string serial, PropertyId property, string deviceKey)
    {
        var device = IoTDevice.Register(SerialNumber.From(serial), ApiKeyHash.CreateFromPlainText(deviceKey), "synthetic");
        device.AssignToProperty(property, InstallationRequestId.From("synthetic-m32-installation"));
        device.RecordInstallation(TechnicianId.From("tech-m32"), property, "synthetic", DateTime.UtcNow);
        return device;
    }
    internal void Register(IServiceCollection services, string userId, bool realPersistence = false)
    {
        ownerUserId = userId;
        services.AddSingleton(Proxy<IProfilesContextFacade>((method, args) => method.Name switch
        {
            nameof(IProfilesContextFacade.GetProfileClaimsAsync) => Task.FromResult<(string ProfileId, string ProfileStatus, string? BusinessRole, string? RoleSubjectId, string? SubscriptionTier)?>(
                args?[0] as string == ownerUserId && ProfileActive ? ("prof-m32", "Active", "HomeOwner", "ho-m32-owner", "FREE") : null),
            nameof(IProfilesContextFacade.IsHomeownerActiveAsync) => Task.FromResult(ProfileActive),
            _ => throw new InvalidOperationException("Unexpected profiles dependency: " + method.Name)
        }));
        services.AddSingleton(Proxy<IIoTDeviceRepository>((method, args) => method.Name switch
        {
            nameof(IIoTDeviceRepository.FindByIdAsync) => Task.FromResult(new[] { Device, ForeignDevice }.SingleOrDefault(d => d.Id == (IoTDeviceId)args![0]!)),
            nameof(IIoTDeviceRepository.FindBySerialNumberAsync) => Task.FromResult(DuplicateAlias ? ForeignDevice : new[] { Device, ForeignDevice }.SingleOrDefault(d => d.SerialNumber == (SerialNumber)args![0]!)),
            _ => throw new InvalidOperationException("Unexpected device write/dependency: " + method.Name)
        }));
        services.AddSingleton(Proxy<IPropertyRepository>((method, args) => method.Name == nameof(IPropertyRepository.FindByIdAndOwnerAsync)
            ? Task.FromResult((PropertyId)args![0]! == Property.Id && (ClientIdentity)args[1]! == Property.Owner ? Property : null)
            : throw new InvalidOperationException("Unexpected property write/dependency: " + method.Name)));
        if (!realPersistence)
        {
            services.RemoveAll<IAlertRepository>();
            services.AddSingleton<IAlertRepository>(Alerts);
            services.RemoveAll<IEquipmentTelemetryRepository>();
            services.AddSingleton<IEquipmentTelemetryRepository>(new TelemetryRepositoryDouble(this));
        }
        services.AddSingleton(Proxy<IDeviceReadingStreamCommandService>(Unexpected));
        services.AddSingleton(Proxy<IRelayCommandService>(Unexpected));
        services.AddSingleton(Proxy<IDeviceReadingStreamRepository>(Unexpected));
        services.AddSingleton(Proxy<Hampcoders.Electrolink.API.Shared.Domain.Repositories.IUnitOfWork>(Unexpected));
    }
    private static object? Unexpected(MethodInfo method, object?[]? args) => throw new InvalidOperationException("Unexpected legacy/relay call: " + method.Name);
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> call) where T : class
    {
        var value = DispatchProxy.Create<T, DeviceAuthorityDouble>();
        ((DeviceAuthorityDouble)(object)value).Call = call;
        return value;
    }
    private sealed class TelemetryRepositoryDouble(EquipmentTelemetryScenario fixture) : IEquipmentTelemetryRepository
    {
        private readonly List<object> pending = new();
        public Task<EquipmentDeviceBinding?> ByEquipment(string id, CancellationToken ct) => Task.FromResult(fixture.Bindings.SingleOrDefault(b => b.EquipmentId == id));
        public Task<EquipmentDeviceBinding?> ByDevice(string id, CancellationToken ct) => Task.FromResult(fixture.Bindings.SingleOrDefault(b => b.DeviceId == id));
        public Task<bool> HasOtherIdentity(string id, CancellationToken ct) => Task.FromResult(fixture.Bindings.Any(b => b.DeviceId == id || b.SerialNumber == id));
        public Task<EquipmentDeviceBinding?> ByEdgeId(string id, CancellationToken ct) => Task.FromResult(fixture.Bindings.SingleOrDefault(b => b.EdgeDeviceId == id));
        public Task<EquipmentMeasurement?> FindReading(string id, string readingId, CancellationToken ct) => Task.FromResult(fixture.Measurements.SingleOrDefault(r => r.BindingId == id && r.ReadingId == readingId));
        public Task<List<EquipmentMeasurement>> Recent(string id, CancellationToken ct) => Task.FromResult(fixture.Measurements.Where(r => r.BindingId == id).OrderByDescending(r => r.CapturedAtUtc).ThenByDescending(r => r.ReadingId).Take(120).ToList());
        public Task<List<EquipmentMeasurement>> History(UserId actor, string storeId, string areaId, string equipmentId,
            DateTime fromUtc, DateTime toUtc, int skip, int take, CancellationToken ct) => Task.FromResult(
            (from reading in fixture.Measurements
             join binding in fixture.Bindings on reading.BindingId equals binding.Id
             where binding.OwnerUserId == actor && binding.EquipmentId == equipmentId
                 && reading.CapturedAtUtc >= fromUtc && reading.CapturedAtUtc < toUtc
             select reading).OrderBy(r => r.CapturedAtUtc).ThenBy(r => r.ReadingId, StringComparer.Ordinal).Skip(skip).Take(take).ToList());
        public void Add(EquipmentDeviceBinding b) => pending.Add(b);
        public void Add(EquipmentMeasurement r) => pending.Add(r);
        public Task Save(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var item in pending)
            {
                switch (item)
                {
                    case EquipmentDeviceBinding b: fixture.Bindings.Add(Clone(b)); break;
                    case EquipmentMeasurement r: fixture.Measurements.Add(Clone(r)); break;
                }
            }
            pending.Clear(); return Task.CompletedTask;
        }
        private static T Clone<T>(T original)
        {
            var copy = (T)Activator.CreateInstance(typeof(T), nonPublic: true)!;
            foreach (var property in typeof(T).GetProperties()) property.SetValue(copy, property.GetValue(original));
            return copy;
        }
    }
}

public class DeviceAuthorityDouble : DispatchProxy
{
    internal Func<MethodInfo, object?[]?, object?> Call { get; set; } = (_, _) => throw new InvalidOperationException("Unconfigured dependency");
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!, args);
}
