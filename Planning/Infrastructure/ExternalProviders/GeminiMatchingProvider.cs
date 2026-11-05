using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Infrastructure;
using Hampcoders.Electrolink.API.Shared.Infrastructure.ExternalProviders;
using Microsoft.Extensions.Options;

namespace Hampcoders.Electrolink.API.Planning.Infrastructure.ExternalProviders;

public class GeminiMatchingProvider : IAIMatchingProvider
{
    private readonly HttpClient _httpClient;
    private readonly GeminiSettings _settings;
    private readonly ILogger<GeminiMatchingProvider> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public GeminiMatchingProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<GeminiSettings> settings,
        ILogger<GeminiMatchingProvider> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScoredCandidate>> ScoreTechnicianCandidatesAsync(
        MatchingContext context, CancellationToken cancellationToken = default)
    {
        var systemPrompt = await File.ReadAllTextAsync(_settings.SystemPromptPath, cancellationToken);
        var userPrompt = systemPrompt + "\n\n" + JsonSerializer.Serialize(context, JsonOptions);

        var requestBody = new GeminiRequest
        {
            Contents =
            [
                new GeminiContent
                {
                    Parts = [new GeminiPart { Text = userPrompt }]
                }
            ],
            GenerationConfig = new GeminiGenerationConfig
            {
                MaxOutputTokens = _settings.MaxTokens
            }
        };

        var url = $"https://generativelanguage.googleapis.com/v1/models/{_settings.ModelId}:generateContent";

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));

                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = JsonContent.Create(requestBody, options: JsonOptions)
                };
                request.Headers.Add("x-goog-api-key", _settings.ApiKey);

                var response = await _httpClient.SendAsync(request, cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cts.Token);
                    _logger.LogWarning("Gemini returned {StatusCode}: {ErrorBody}",
                        response.StatusCode, errorBody);
                }
                response.EnsureSuccessStatusCode();

                var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiResponse>(JsonOptions, cts.Token);
                var text = geminiResponse?.Candidates?.FirstOrDefault()
                    ?.Content?.Parts?.FirstOrDefault()
                    ?.Text;

                if (text is null)
                {
                    _logger.LogWarning("Gemini returned empty response on attempt {Attempt}", attempt);
                    if (attempt < 3) continue;
                    return [];
                }

                var candidates = JsonSerializer.Deserialize<List<ScoredCandidate>>(text, JsonOptions);
                return candidates?.AsReadOnly() ?? (IReadOnlyList<ScoredCandidate>)Array.Empty<ScoredCandidate>();
            }
            catch (OperationCanceledException) when (attempt < 3)
            {
                _logger.LogWarning("Gemini matching timed out on attempt {Attempt}, retrying...", attempt);
            }
            catch (Exception ex) when (attempt < 3)
            {
                _logger.LogWarning(ex, "Gemini matching failed on attempt {Attempt}, retrying...", attempt);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
            }
            catch (Exception ex)
            {
                throw new AIProviderException("Gemini", "Failed to score technician candidates after 3 attempts", ex);
            }
        }

        return [];
    }

    // ── Gemini API DTOs ───────────────────────────────────

    private sealed record GeminiRequest
    {
        [JsonPropertyName("contents")]
        public required IReadOnlyList<GeminiContent> Contents { get; init; }

        [JsonPropertyName("generation_config")]
        public GeminiGenerationConfig? GenerationConfig { get; init; }
    }

    private sealed record GeminiContent
    {
        [JsonPropertyName("parts")]
        public required IReadOnlyList<GeminiPart> Parts { get; init; }
    }

    private sealed record GeminiPart
    {
        [JsonPropertyName("text")]
        public required string Text { get; init; }
    }

    private sealed record GeminiGenerationConfig
    {
        [JsonPropertyName("maxOutputTokens")]
        public int MaxOutputTokens { get; init; }
    }

    private sealed record GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public IReadOnlyList<GeminiCandidate>? Candidates { get; init; }
    }

    private sealed record GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; init; }
    }
}
