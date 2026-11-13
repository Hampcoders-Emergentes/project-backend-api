using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Assets.Interfaces.ACL;
using Hampcoders.Electrolink.API.Monitoring.Interfaces.ACL;
using Hampcoders.Electrolink.API.Planning.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Planning.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Planning.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Planning.Domain.Repositories;
using Hampcoders.Electrolink.API.Processing.Interfaces.ACL;
using Hampcoders.Electrolink.API.Processing.Interfaces.ACL.Services;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.ACL;
using Microsoft.Extensions.Logging.Abstractions;
using static Electrolink.Backend.UnitTests.Planning.PlanningTestData;

namespace Electrolink.Backend.UnitTests.Planning.Application.Internal.QueryServices;

public class ServiceSuggestionQueryServiceTests
{
    [Fact]
    public async Task GetActiveByClientIdAsync_ReturnsRepositorySuggestions()
    {
        // Arrange
        var suggestion = ServiceSuggestion.Create(Homeowner, null, "prop-1", "an-1", "dev-1", "SPIKE", "LOW", DateTime.UtcNow.AddDays(1));
        var repository = new Fake<IServiceSuggestionRepository>()
            .Returns(nameof(IServiceSuggestionRepository.FindActiveByClientIdAsync), new[] { suggestion });
        var sut = new ServiceSuggestionQueryService(repository.Object);

        // Act
        var result = await sut.GetActiveByClientIdAsync(Homeowner);

        // Assert
        Assert.Same(suggestion, Assert.Single(result));
    }
}
