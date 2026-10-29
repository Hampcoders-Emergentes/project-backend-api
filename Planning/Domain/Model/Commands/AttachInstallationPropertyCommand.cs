using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Planning.Domain.Model.Commands;

// Actor is the authenticated IAM user; client, subscription and technician are never taken from the request body.
public record AttachInstallationPropertyCommand(UserId Actor, string RequestId, string PropertyId);
