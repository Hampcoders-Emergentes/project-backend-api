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

namespace Electrolink.Backend.UnitTests.Profiles.Application.Internal.OutboundServices;

public class ProfilesOutboundServicesTests
{
    [Fact]
    public async Task ExternalAssetService_CreateTechnicianInventoryAsyncWhenExists_ThrowsInvalidOperationException()
    {
        // Arrange
        var assets = new Fake<IAssetsContextFacade>().Returns(nameof(IAssetsContextFacade.ExistsInventoryForTechnicianAsync), true);
        var sut = new ExternalAssetService(assets.Object);

        // Act
        var act = () => sut.CreateTechnicianInventoryAsync(Ids.Technician);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task ExternalAssetService_CreateTechnicianInventoryAsync_ReturnsNewInventoryId()
    {
        // Arrange
        var assets = new Fake<IAssetsContextFacade>().Returns(nameof(IAssetsContextFacade.CreateTechnicianInventoryAsync), "inv-1");
        var sut = new ExternalAssetService(assets.Object);

        // Act
        var result = await sut.CreateTechnicianInventoryAsync(Ids.Technician);

        // Assert
        Assert.Equal("inv-1", result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExternalIamService_UserExistsAsync_ReturnsIamResult(bool exists)
    {
        // Arrange
        var iam = new Fake<IIamContextFacade>().Returns(nameof(IIamContextFacade.UserExistsAsync), exists);
        var sut = new ExternalIamService(iam.Object);

        // Act
        var result = await sut.UserExistsAsync(Ids.User);

        // Assert
        Assert.Equal(exists, result);
    }
}
