namespace Hampcoders.Electrolink.API.IAM.Domain.Model.Exceptions;

// M3.28: the account exists and the credentials are valid, but it is not Active (Suspended or PendingVerification).
public sealed class InactiveAccountException() : Exception("Account is not active");
