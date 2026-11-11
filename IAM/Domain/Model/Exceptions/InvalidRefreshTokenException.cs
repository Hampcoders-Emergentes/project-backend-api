namespace Hampcoders.Electrolink.API.IAM.Domain.Model.Exceptions;

public sealed class InvalidRefreshTokenException(string message = "Invalid refresh token") : Exception(message);
