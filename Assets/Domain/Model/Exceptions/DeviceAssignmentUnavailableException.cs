namespace Hampcoders.Electrolink.API.Assets.Domain.Model.Exceptions;

public sealed class DeviceAssignmentUnavailableException() : Exception("Resource not found.");
public sealed class DeviceAssignmentConflictException() : Exception("Device is not free stock.");
