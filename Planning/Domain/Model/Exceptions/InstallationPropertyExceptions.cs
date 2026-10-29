namespace Hampcoders.Electrolink.API.Planning.Domain.Model.Exceptions;

public sealed class InstallationPropertyActorDeniedException() : Exception("Forbidden.");
public sealed class InstallationRequestUnavailableException() : Exception("Resource not found.");
public sealed class InstallationRequestConflictException() : Exception("Installation request is not awaiting a property.");
