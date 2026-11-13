using System.Net;
using System.Text;
using System.Text.Json;
using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Assets.Interfaces.ACL;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Events;
using Hampcoders.Electrolink.API.IAM.Interfaces.ACL;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Events;
using Hampcoders.Electrolink.API.Profiles.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Profiles.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Profiles.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Profiles.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Profiles.Domain.Repositories;
using Hampcoders.Electrolink.API.Profiles.Domain.Services;
using Hampcoders.Electrolink.API.Profiles.Infrastructure.ExternalProviders;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Events;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Electrolink.Backend.UnitTests.Profiles.ProfilesTestData;
using ServiceCompletedEventHandler = Hampcoders.Electrolink.API.Profiles.Application.Internal.EventHandlers.ServiceCompletedEventHandler;

namespace Electrolink.Backend.UnitTests.Profiles.Application.Internal.CommandServices;

public sealed class DeviceOnboardingServiceTests : IDisposable
{
    private readonly string _promptPath = Path.Combine(Path.GetTempPath(), $"onboarding-{Guid.NewGuid():N}.txt");
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly List<HttpRequestMessage> _requests = [];
    private Func<HttpResponseMessage> _respond = () => new HttpResponseMessage(HttpStatusCode.OK);

    public DeviceOnboardingServiceTests() => File.WriteAllText(_promptPath, "You size thresholds.");

    public void Dispose()
    {
        _cache.Dispose();
        File.Delete(_promptPath);
    }

    private DeviceOnboardingService CreateSut()
    {
        var factory = new Fake<IHttpClientFactory>().On(nameof(IHttpClientFactory.CreateClient), _ => new HttpClient(new StubHandler(this)));
        return new DeviceOnboardingService(factory.Object,
            Options.Create(new ProfilesAISettings { ApiKey = "k", ModelId = "m", SystemPromptPath = _promptPath, TimeoutSeconds = 5 }),
            _cache, NullLogger<DeviceOnboardingService>.Instance);
    }

    private static AnalyzeDeviceOnboardingRequest Request(float voltage = 220) => new("Fridge", voltage, 24, 3, "House", "Food");

    private static HttpResponseMessage GroqReply(string? content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content } } } }),
            Encoding.UTF8, "application/json")
    };

    private const string AiJson =
        "{\"reasoning\":\"Fridge load\",\"narrative\":\"Sized for a fridge\",\"suggestedThresholds\":{\"nominalVoltage\":220,\"maxConsumptionWatts\":800,\"maxCurrentAmps\":4,\"minPowerFactor\":0.9,\"nominalFrequency\":60,\"disconnectionThresholdMin\":5}}";

    [Fact]
    public async Task AnalyzeAsync_WithValidAiReply_ReturnsAiThresholdsWithSessionId()
    {
        // Arrange
        _respond = () => GroqReply(AiJson);
        var sut = CreateSut();

        // Act
        var result = await sut.AnalyzeAsync(Ids.User, Request());

        // Assert
        Assert.Equal(("Fridge load", 800f), (result.Reasoning, result.SuggestedThresholds.MaxConsumptionWatts));
        Assert.False(string.IsNullOrEmpty(result.AnalysisId));
    }

    [Fact]
    public async Task AnalyzeAsync_WhenAiReturnsNoContent_FallsBackToSafeDefaults()
    {
        // Arrange
        _respond = () => GroqReply(null);
        var sut = CreateSut();

        // Act
        var result = await sut.AnalyzeAsync(Ids.User, Request(220));

        // Assert
        Assert.Equal((2500f, 13.6f, 50f), (result.SuggestedThresholds.MaxConsumptionWatts, result.SuggestedThresholds.MaxCurrentAmps,
            result.SuggestedThresholds.NominalFrequency));
        Assert.Equal(3, _requests.Count);
    }

    [Fact]
    public async Task AnalyzeAsync_WithLowVoltageAndNoAiContent_UsesSixtyHertzDefault()
    {
        // Arrange
        _respond = () => GroqReply(null);
        var sut = CreateSut();

        // Act
        var result = await sut.AnalyzeAsync(Ids.User, Request(110));

        // Assert
        Assert.Equal((110f, 60f), (result.SuggestedThresholds.NominalVoltage, result.SuggestedThresholds.NominalFrequency));
    }

    [Fact]
    public async Task AnalyzeAsync_SendsBearerTokenToAiProvider()
    {
        // Arrange
        _respond = () => GroqReply(AiJson);
        var sut = CreateSut();

        // Act
        await sut.AnalyzeAsync(Ids.User, Request());

        // Assert
        Assert.Equal(("Bearer", "k"), (_requests.Single().Headers.Authorization!.Scheme, _requests.Single().Headers.Authorization!.Parameter));
    }

    [Fact]
    public async Task AdjustAnalysisAsync_WithUnknownSession_ThrowsKeyNotFoundException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.AdjustAnalysisAsync(Ids.User, "missing", "lower it");

        // Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(act);
    }

    [Fact]
    public async Task AdjustAnalysisAsync_BySessionOwnerOfAnotherUser_ThrowsKeyNotFoundException()
    {
        // Arrange
        _respond = () => GroqReply(AiJson);
        var sut = CreateSut();
        var analysis = await sut.AnalyzeAsync(Ids.User, Request());

        // Act
        var act = () => sut.AdjustAnalysisAsync(Ids.OtherUser, analysis.AnalysisId!, "lower it");

        // Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(act);
    }

    [Fact]
    public async Task AdjustAnalysisAsync_WithOwnSession_ReturnsAdjustedAnalysis()
    {
        // Arrange
        _respond = () => GroqReply(AiJson);
        var sut = CreateSut();
        var analysis = await sut.AnalyzeAsync(Ids.User, Request());

        // Act
        var result = await sut.AdjustAnalysisAsync(Ids.User, analysis.AnalysisId!, "lower it");

        // Assert
        Assert.Equal(4f, result.SuggestedThresholds.MaxCurrentAmps);
    }

    private sealed class StubHandler(DeviceOnboardingServiceTests owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            owner._requests.Add(request);
            return Task.FromResult(owner._respond());
        }
    }
}
