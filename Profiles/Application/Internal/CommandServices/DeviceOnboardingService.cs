using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hampcoders.Electrolink.API.Profiles.Domain.Services;
using Hampcoders.Electrolink.API.Profiles.Infrastructure.ExternalProviders;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Hampcoders.Electrolink.API.Profiles.Application.Internal.CommandServices;

public class DeviceOnboardingService(
    IHttpClientFactory httpClientFactory,
    IOptions<ProfilesAISettings> settings,
    IMemoryCache sessions,
    ILogger<DeviceOnboardingService> logger)
    : IDeviceOnboardingService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly MemoryCacheEntryOptions SessionLifetime = new()
    {
        SlidingExpiration = TimeSpan.FromMinutes(30),
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2)
    };

    // Keyed by owner too: another user's analysis id simply finds no session.
    private static string SessionKey(string ownerUserId, string analysisId) => $"device-onboarding:{ownerUserId}:{analysisId}";

    public async Task<DeviceOnboardingAnalysis> AnalyzeAsync(
        string ownerUserId,
        AnalyzeDeviceOnboardingRequest request,
        CancellationToken cancellationToken = default)
    {
        var prompt = await File.ReadAllTextAsync(settings.Value.SystemPromptPath, cancellationToken);
        var userMessage = JsonSerializer.Serialize(request, JsonOptions);

        var analysis = await CallGroqAsync(prompt, userMessage, cancellationToken)
                       ?? DefaultAnalysis(request);

        var analysisId = Guid.NewGuid().ToString();
        sessions.Set(SessionKey(ownerUserId, analysisId), request, SessionLifetime);

        return analysis with { AnalysisId = analysisId };
    }

    public async Task<DeviceOnboardingAnalysis> AdjustAnalysisAsync(
        string ownerUserId,
        string analysisId,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        if (!sessions.TryGetValue(SessionKey(ownerUserId, analysisId), out AnalyzeDeviceOnboardingRequest? request) || request is null)
            throw new KeyNotFoundException("Sesión de análisis no encontrada");

        var prompt = await File.ReadAllTextAsync(settings.Value.SystemPromptPath, cancellationToken);
        var context = JsonSerializer.Serialize(request, JsonOptions);
        var fullUserMessage = $"DATOS ORIGINALES: {context}\n\nEL USUARIO PIDE: {userMessage}\n\nAjusta los thresholds según lo solicitado.";

        return await CallGroqAsync(prompt, fullUserMessage, cancellationToken)
               ?? DefaultAnalysis(request);
    }

    private async Task<DeviceOnboardingAnalysis?> CallGroqAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        var requestBody = new GroqRequest
        {
            Model = settings.Value.ModelId,
            Messages =
            [
                new GroqMessage { Role = "system", Content = systemPrompt },
                new GroqMessage { Role = "user", Content = userMessage }
            ],
            MaxTokens = settings.Value.MaxTokens
        };

        var url = "https://api.groq.com/openai/v1/chat/completions";

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var httpClient = httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(settings.Value.TimeoutSeconds);
                httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", settings.Value.ApiKey);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(settings.Value.TimeoutSeconds));

                var response = await httpClient.PostAsJsonAsync(url, requestBody, JsonOptions, cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cts.Token);
                    logger.LogWarning("Groq returned {StatusCode}: {ErrorBody}",
                        response.StatusCode, errorBody);
                }
                response.EnsureSuccessStatusCode();

                var groqResponse = await response.Content.ReadFromJsonAsync<GroqResponse>(JsonOptions, cts.Token);
                var text = groqResponse?.Choices?.FirstOrDefault()
                    ?.Message?.Content;

                if (text is null)
                {
                    logger.LogWarning("Groq returned empty response on attempt {Attempt}", attempt);
                    if (attempt < 3) continue;
                    return null;
                }

                var analysis = JsonSerializer.Deserialize<AiAnalysisResponse>(text, JsonOptions);
                if (analysis is null || analysis.SuggestedThresholds is null)
                {
                    logger.LogWarning("Failed to parse Groq analysis on attempt {Attempt}", attempt);
                    if (attempt < 3) continue;
                    return null;
                }

                return new DeviceOnboardingAnalysis(
                    analysis.Reasoning ?? "Análisis generado automáticamente.",
                    analysis.Narrative ?? analysis.Reasoning ?? "",
                    analysis.SuggestedThresholds
                );
            }
            catch (OperationCanceledException) when (attempt < 3)
            {
                logger.LogWarning("Groq onboarding timed out on attempt {Attempt}, retrying...", attempt);
            }
            catch (Exception ex) when (attempt < 3)
            {
                logger.LogWarning(ex, "Groq onboarding failed on attempt {Attempt}, retrying...", attempt);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Groq onboarding failed after 3 attempts");
            }
        }

        return null;
    }

    private DeviceOnboardingAnalysis DefaultAnalysis(AnalyzeDeviceOnboardingRequest request)
    {
        var maxCurrent = request.NominalVoltage > 0
            ? (float)Math.Round(2500f / request.NominalVoltage * 1.2f, 1)
            : 15f;

        return new DeviceOnboardingAnalysis(
            "No se pudo contactar el servicio de IA. Se usaron valores predeterminados seguros.",
            "No pudimos contactar al servicio de IA en este momento. Usamos valores seguros por defecto. Puedes ajustarlos manualmente.",
            new SuggestedThresholds(
                request.NominalVoltage > 0 ? request.NominalVoltage : 220f,
                2500f,
                maxCurrent,
                0.85f,
                request.NominalVoltage >= 200 ? 50f : 60f,
                10
            )
        );
    }

    private sealed record GroqRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("messages")]
        public required IReadOnlyList<GroqMessage> Messages { get; init; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; init; }
    }

    private sealed record GroqMessage
    {
        [JsonPropertyName("role")]
        public required string Role { get; init; }

        [JsonPropertyName("content")]
        public required string Content { get; init; }
    }

    private sealed record GroqResponse
    {
        [JsonPropertyName("choices")]
        public IReadOnlyList<GroqChoice>? Choices { get; init; }
    }

    private sealed record GroqChoice
    {
        [JsonPropertyName("message")]
        public GroqMessage? Message { get; init; }
    }

    private sealed record AiAnalysisResponse
    {
        [JsonPropertyName("reasoning")]
        public string? Reasoning { get; init; }

        [JsonPropertyName("narrative")]
        public string? Narrative { get; init; }

        [JsonPropertyName("suggestedThresholds")]
        public SuggestedThresholds? SuggestedThresholds { get; init; }
    }
}
