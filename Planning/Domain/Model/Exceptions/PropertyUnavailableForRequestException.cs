namespace Hampcoders.Electrolink.API.Planning.Domain.Model.Exceptions;

// The property does not exist or does not belong to the request's client.
public sealed class PropertyUnavailableForRequestException() : Exception("Resource not found.");
