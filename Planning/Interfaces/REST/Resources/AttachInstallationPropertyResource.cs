using System.ComponentModel.DataAnnotations;

namespace Hampcoders.Electrolink.API.Planning.Interfaces.REST.Resources;

// Only the property is chosen by the caller; client, user, technician and subscription come from the server.
public record AttachInstallationPropertyResource(
    [Required, MaxLength(100), RegularExpression("^prop-[A-Za-z0-9-]+$")] string PropertyId);

public record InstallationPropertyAttachedResource(string RequestId, string PropertyId, string Status, string Matching);
