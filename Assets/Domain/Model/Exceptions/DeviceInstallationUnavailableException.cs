namespace Hampcoders.Electrolink.API.Assets.Domain.Model.Exceptions;

public sealed class DeviceInstallationUnavailableException() : Exception("Resource not found.");
public sealed class DeviceInstallationStateException() : Exception("Device is not awaiting installation.");
