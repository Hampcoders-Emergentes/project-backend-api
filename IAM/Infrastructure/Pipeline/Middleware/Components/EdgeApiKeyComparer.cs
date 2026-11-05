using System.Security.Cryptography;
using System.Text;

namespace Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Components;

internal static class EdgeApiKeyComparer
{
    // FixedTimeEquals returns early when lengths differ, so both keys are hashed to a fixed length first.
    public static bool Matches(string? provided, string? expected) =>
        !string.IsNullOrEmpty(provided)
        && !string.IsNullOrEmpty(expected)
        && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
