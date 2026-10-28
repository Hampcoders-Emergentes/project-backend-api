namespace Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes;

/// <summary>
/// Allows an Edge API key as an alternative to a human token on this endpoint.
/// Human authorization requirements still take precedence; this metadata does not create a user.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AllowEdgeApiKeyAttribute : Attribute
{
}
