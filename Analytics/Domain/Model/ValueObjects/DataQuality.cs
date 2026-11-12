namespace Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;

/// <summary>
///     Whether an analysis had enough telemetry to be meaningful. An empty result with INSUFFICIENT_DATA means
///     "we cannot tell", never "healthy" or "no consumption".
/// </summary>
public static class DataQuality
{
    public const string Sufficient = "SUFFICIENT";
    public const string InsufficientData = "INSUFFICIENT_DATA";

    public static string Evaluate(int sampleCount, int minimumSamples) =>
        sampleCount >= minimumSamples ? Sufficient : InsufficientData;
}
