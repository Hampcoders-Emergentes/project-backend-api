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

namespace Electrolink.Backend.UnitTests.Planning.Application.Internal.OutboundServices;

public class ComponentTypeValidatorTests
{
    [Fact]
    public void ValidateAll_WhenEveryTypeIsActive_DoesNotThrow()
    {
        // Arrange
        var assets = new Fake<IAssetsContextFacade>().Returns(nameof(IAssetsContextFacade.ComponentTypeExistsAndIsActiveAsync), true);
        var sut = new ComponentTypeValidator(new ExternalAssetsService(assets.Object));

        // Act
        var exception = Record.Exception(() => sut.ValidateAll([ComponentRequirementItem.Create("ctype-1", "Breaker", 1, true)]));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateAll_WhenTypeIsInactive_ThrowsInvalidComponentTypeException()
    {
        // Arrange
        var sut = new ComponentTypeValidator(new ExternalAssetsService(new Fake<IAssetsContextFacade>().Object));

        // Act
        var act = () => sut.ValidateAll([ComponentRequirementItem.Create("ctype-1", "Breaker", 1, true)]);

        // Assert
        Assert.Throws<InvalidComponentTypeException>(act);
    }
}
