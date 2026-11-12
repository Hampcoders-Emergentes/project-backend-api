namespace Hampcoders.Electrolink.API.IAM.Interfaces.REST.Resources;

public record AuthenticatedUserResource(string UserId, string Email, string Token, string RefreshToken);
