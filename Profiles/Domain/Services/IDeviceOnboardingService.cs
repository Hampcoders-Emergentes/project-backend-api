namespace Hampcoders.Electrolink.API.Profiles.Domain.Services;

public record AnalyzeDeviceOnboardingRequest(
    string DeviceType,
    float NominalVoltage,
    float DailyUsageHours,
    int Occupants,
    string LocationType,
    string PrimaryUse
);

public record SuggestedThresholds(
    float NominalVoltage,
    float MaxConsumptionWatts,
    float MaxCurrentAmps,
    float MinPowerFactor,
    float NominalFrequency,
    int DisconnectionThresholdMin
);

public record DeviceOnboardingAnalysis(
    string Reasoning,
    string Narrative,
    SuggestedThresholds SuggestedThresholds,
    string? AnalysisId = null
);

public interface IDeviceOnboardingService
{
    Task<DeviceOnboardingAnalysis> AnalyzeAsync(
        string ownerUserId,
        AnalyzeDeviceOnboardingRequest request,
        CancellationToken cancellationToken = default);

    Task<DeviceOnboardingAnalysis> AdjustAnalysisAsync(
        string ownerUserId,
        string analysisId,
        string userMessage,
        CancellationToken cancellationToken = default);
}
