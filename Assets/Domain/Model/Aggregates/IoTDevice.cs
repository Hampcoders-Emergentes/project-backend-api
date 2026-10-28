using Hampcoders.Electrolink.API.Assets.Domain.Model.Events;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;

public class IoTDevice : BaseAggregateRoot
{
    public IoTDeviceId Id { get; private set; } = null!;
    public SerialNumber SerialNumber { get; private set; } = null!;
    public ApiKeyHash ApiKeyHash { get; private set; } = null!;
    public string FirmwareVersion { get; private set; } = string.Empty;
    public EDeviceStatus Status { get; private set; }
    public TechnicianId? StockTechnicianId { get; private set; }
    public PropertyId? AssignedPropertyId { get; private set; }
    public InstallationRequestId? InstallationRequestId { get; private set; }
    public TechnicianId? InstalledByTechnicianId { get; private set; }
    public StaffMemberId? InstalledByStaffMemberId { get; private set; }
    public DateTime? InstalledAt { get; private set; }
    public EConnectionStatus ConnectionStatus { get; private set; }
    public DateTime? LastReadingAt { get; private set; }
    public string? MaintenanceReason { get; private set; }
    public DateTime? ExpectedReturnDate { get; private set; }

    private IoTDevice() { }

    public static IoTDevice Register(SerialNumber serialNumber, ApiKeyHash apiKeyHash, string firmwareVersion)
    {
        if (serialNumber is null)
            throw new ArgumentNullException(nameof(serialNumber));
        if (apiKeyHash is null)
            throw new ArgumentNullException(nameof(apiKeyHash));
        if (string.IsNullOrWhiteSpace(firmwareVersion))
            throw new ArgumentException("FirmwareVersion cannot be empty.", nameof(firmwareVersion));

        var device = new IoTDevice
        {
            Id = IoTDeviceId.NewId(),
            SerialNumber = serialNumber,
            ApiKeyHash = apiKeyHash,
            FirmwareVersion = firmwareVersion,
            Status = EDeviceStatus.InStock,
            ConnectionStatus = EConnectionStatus.Disconnected,
        };

        device.RaiseDomainEvent(new IoTDeviceRegisteredEvent(
            device.Id.Value, device.SerialNumber.Value, device.FirmwareVersion, DateTime.UtcNow));

        return device;
    }

    public static IoTDevice RegisterForTechnician(SerialNumber serialNumber, ApiKeyHash apiKeyHash,
        string firmwareVersion, TechnicianId technicianId)
    {
        ArgumentNullException.ThrowIfNull(technicianId);
        var device = Register(serialNumber, apiKeyHash, firmwareVersion);
        device.StockTechnicianId = technicianId;
        return device;
    }

    public void AssignToProperty(PropertyId propertyId, InstallationRequestId installationRequestId)
    {
        if (Status != EDeviceStatus.InStock)
            throw new InvalidOperationException(
                $"Device must be IN_STOCK to be assigned. Current status: {Status}");

        AssignedPropertyId = propertyId;
        InstallationRequestId = installationRequestId;
        Status = EDeviceStatus.Assigned;

        RaiseDomainEvent(new DeviceAssignedToPropertyEvent(
            Id.Value, SerialNumber.Value, propertyId.Value,
            installationRequestId.Value, DateTime.UtcNow));
    }

    public void RecordInstallation(TechnicianId technicianId, PropertyId propertyId, string firmwareVersion, DateTime installedAt)
    {
        if (Status != EDeviceStatus.Assigned)
            throw new InvalidOperationException(
                $"Device must be ASSIGNED to record installation. Current status: {Status}");

        if (AssignedPropertyId is null || AssignedPropertyId.Value != propertyId.Value)
            throw new InvalidOperationException(
                "PropertyId does not match the assigned property of this device.");

        InstalledByTechnicianId = technicianId;
        InstalledAt = installedAt;
        FirmwareVersion = firmwareVersion;
        Status = EDeviceStatus.Installed;

        RaiseDomainEvent(new DeviceInstalledEvent(
            Id.Value, SerialNumber.Value, propertyId.Value,
            technicianId.Value, InstallationRequestId!.Value,
            firmwareVersion, installedAt));
    }

    public void RecordInstallation(StaffMemberId staffMemberId, PropertyId propertyId, string firmwareVersion, DateTime installedAt)
    {
        if (Status != EDeviceStatus.Assigned)
            throw new InvalidOperationException(
                $"Device must be ASSIGNED to record installation. Current status: {Status}");

        if (AssignedPropertyId is null || AssignedPropertyId.Value != propertyId.Value)
            throw new InvalidOperationException(
                "PropertyId does not match the assigned property of this device.");

        InstalledByStaffMemberId = staffMemberId;
        InstalledAt = installedAt;
        FirmwareVersion = firmwareVersion;
        Status = EDeviceStatus.Installed;

        RaiseDomainEvent(new DeviceInstalledEvent(
            Id.Value, SerialNumber.Value, propertyId.Value,
            staffMemberId.Value, InstallationRequestId!.Value,
            firmwareVersion, installedAt));
    }

    public void UpdateConnectionStatus(EConnectionStatus newStatus, DateTime? lastReadingAt)
    {
        if (Status != EDeviceStatus.Installed && Status != EDeviceStatus.Maintenance)
            throw new InvalidOperationException(
                "Connection status can only be updated for INSTALLED or MAINTENANCE devices.");

        var previous = ConnectionStatus;
        ConnectionStatus = newStatus;

        if (lastReadingAt.HasValue &&
            (LastReadingAt is null || lastReadingAt.Value > LastReadingAt.Value))
        {
            LastReadingAt = lastReadingAt.Value;
        }

        RaiseDomainEvent(new DeviceConnectionStatusUpdatedEvent(
            Id.Value, AssignedPropertyId!.Value, previous, newStatus,
            LastReadingAt, DateTime.UtcNow));
    }

    public void SendToMaintenance(string reason, DateTime? expectedReturnDate)
    {
        if (Status != EDeviceStatus.Installed)
            throw new InvalidOperationException(
                $"Device must be INSTALLED to send to maintenance. Current status: {Status}");

        MaintenanceReason = reason;
        ExpectedReturnDate = expectedReturnDate;
        Status = EDeviceStatus.Maintenance;
        ConnectionStatus = EConnectionStatus.Disconnected;

        RaiseDomainEvent(new DeviceSentToMaintenanceEvent(
            Id.Value, AssignedPropertyId!.Value, reason, expectedReturnDate, DateTime.UtcNow));
    }

    public void Reinstall(TechnicianId technicianId, string firmwareVersion, DateTime reinstalledAt)
    {
        if (Status != EDeviceStatus.Maintenance)
            throw new InvalidOperationException(
                $"Device must be in MAINTENANCE to reinstall. Current status: {Status}");

        InstalledByTechnicianId = technicianId;
        FirmwareVersion = firmwareVersion;
        MaintenanceReason = null;
        ExpectedReturnDate = null;
        Status = EDeviceStatus.Installed;
        ConnectionStatus = EConnectionStatus.Disconnected;

        RaiseDomainEvent(new DeviceReinstalledEvent(
            Id.Value, AssignedPropertyId!.Value, technicianId.Value,
            firmwareVersion, reinstalledAt));
    }

    public void Decommission(string reason)
    {
        if (Status == EDeviceStatus.Decommissioned) return;

        var previousStatus = Status;
        Status = EDeviceStatus.Decommissioned;
        ConnectionStatus = EConnectionStatus.Disconnected;

        RaiseDomainEvent(new DeviceDecommissionedEvent(
            Id.Value, SerialNumber.Value,
            AssignedPropertyId?.Value, previousStatus, reason, DateTime.UtcNow));
    }
}
