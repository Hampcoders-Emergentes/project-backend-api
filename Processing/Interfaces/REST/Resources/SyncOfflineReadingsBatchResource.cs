using System.ComponentModel.DataAnnotations;

namespace Hampcoders.Electrolink.API.Processing.Interfaces.REST.Resources;

/// <param name="PanelId">Optional identifier of the edge panel that buffered the readings (for traceability).</param>
/// <param name="Readings">Readings cached during the outage, in any order; each keeps its original capture timestamp.</param>
public sealed record SyncOfflineReadingsBatchResource(
    [StringLength(200)] string? PanelId,
    [Required, MinLength(1), MaxLength(1000)] IReadOnlyList<IngestReadingResource> Readings);
