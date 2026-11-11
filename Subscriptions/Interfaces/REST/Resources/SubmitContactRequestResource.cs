using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hampcoders.Electrolink.API.Subscriptions.Interfaces.REST.Resources;

/// <summary>Demo request submitted from the public site.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SubmitContactRequestResource(
    [Required, StringLength(120)] string Name,
    [Required, StringLength(160)] string Company,
    [Required, StringLength(254), EmailAddress] string Email,
    [StringLength(30)] string? Phone,
    [Required, StringLength(2000)] string Message);
