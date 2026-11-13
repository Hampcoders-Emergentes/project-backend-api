using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Planning.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Services;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Infrastructure;
using Hampcoders.Electrolink.API.Shared.Infrastructure.ExternalProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Electrolink.Backend.UnitTests.Planning.PlanningTestData;

namespace Electrolink.Backend.UnitTests.Planning.Application.Internal.CommandServices;

public class HybridMatchingServiceTests
{
    private const string Near = "tech-near";
    private const string Far = "tech-far";
    private readonly Fake<IAIMatchingProvider> _ai = new();

    private HybridMatchingService CreateSut() => new(_ai.Object, Options.Create(new OpenAISettings { MatchingTimeoutSeconds = 5 }),
        new ExternalProfilesService(new Fake<IProfilesContextFacade>().Object), NullLogger<HybridMatchingService>.Instance);

    private static Task<MatchingResult> Match(HybridMatchingService sut, IReadOnlyList<EnrichedCandidate> candidates, bool priority = false)
    {
        var request = PendingAssignment(priority);
        return sut.FindBestCandidateAsync(request.RequestId, candidates, request);
    }

    [Fact]
    public async Task FindBestCandidateAsync_WhenNoCandidateHasComponents_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => Match(sut, [Candidate(Near, hasComponents: false)]);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task FindBestCandidateAsync_ForPriorityRequestWithOnlySlowResponders_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => Match(sut, [Candidate(Near, responseMinutes: 90)], priority: true);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task FindBestCandidateAsync_WhenAiScoresCandidates_ReturnsAiTopCandidate()
    {
        // Arrange
        _ai.Returns(nameof(IAIMatchingProvider.ScoreTechnicianCandidatesAsync),
            new List<ScoredCandidate> { new(Near, 0.4, "close"), new(Far, 0.9, "expert") });
        var sut = CreateSut();

        // Act
        var result = await Match(sut, [Candidate(Near), Candidate(Far, distanceKm: 80)]);

        // Assert
        Assert.Equal((Far, MatchingMethod.AIEnhanced), (result.BestCandidate.TechnicianId, result.Score.Method));
    }

    [Fact]
    public async Task FindBestCandidateAsync_WhenAiFails_FallsBackToDeterministicScoring()
    {
        // Arrange
        _ai.Throws(nameof(IAIMatchingProvider.ScoreTechnicianCandidatesAsync), new HttpRequestException("503"));
        var sut = CreateSut();

        // Act
        var result = await Match(sut, [Candidate(Far, distanceKm: 90), Candidate(Near, distanceKm: 2)]);

        // Assert
        Assert.Equal((Near, MatchingMethod.AIFallback), (result.BestCandidate.TechnicianId, result.Score.Method));
    }

    [Fact]
    public async Task FindBestCandidateAsync_WhenAiReturnsNoScores_FallsBackToDeterministicScoring()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await Match(sut, [Candidate(Near, rating: 5)]);

        // Assert
        Assert.Equal(MatchingMethod.AIFallback, result.Score.Method);
    }

    [Fact]
    public async Task FindBestCandidateAsync_WithDeterministicScoring_ScoresEveryCandidateWithinUnitRange()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await Match(sut, [Candidate(Near, distanceKm: 0, rating: 5, experience: 20, responseMinutes: 0), Candidate(Far, distanceKm: 200)]);

        // Assert
        Assert.All(result.AllScored, s => Assert.InRange(s.Score, 0, 1));
        Assert.Equal(0.9, result.AllScored.Single(s => s.TechnicianId == Near).Score, 6);
    }
}
