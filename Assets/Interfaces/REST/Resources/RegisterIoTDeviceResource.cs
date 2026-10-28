using System.ComponentModel.DataAnnotations;

namespace Hampcoders.Electrolink.API.Assets.Interfaces.REST.Resources;

public record RegisterIoTDeviceResource(
    [Required, StringLength(100)] string SerialNumber,
    [Required] string PlainTextApiKey,
    [Required, StringLength(50)] string FirmwareVersion);
