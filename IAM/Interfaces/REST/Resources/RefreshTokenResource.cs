using System.ComponentModel.DataAnnotations;

namespace Hampcoders.Electrolink.API.IAM.Interfaces.REST.Resources;

public record RefreshTokenResource([property: Required] string RefreshToken);
