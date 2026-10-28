namespace Hampcoders.Electrolink.API.Assets.Domain.Model.Exceptions;

// Same result for unknown, unassigned or unauthorized inventory.
public sealed class DeviceRetirementUnavailableException() : Exception("Resource not found.");
