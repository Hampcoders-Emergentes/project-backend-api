using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.IAM.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.IAM.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Events;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Queries;
using Hampcoders.Electrolink.API.IAM.Domain.Repositories;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.StoreManagement.Interfaces.ACL;
using Microsoft.Extensions.Logging.Abstractions;
using static Electrolink.Backend.UnitTests.IAM.IamTestData;

namespace Electrolink.Backend.UnitTests.IAM.Application.Internal.OutboundServices;

public class IamOutboundServicesTests
{
    [Fact]
    public async Task ExternalProfilesService_GetProfileClaimsAsync_ReturnsFacadeClaims()
    {
        // Arrange
        var claims = ("prof-1", "Active", (string?)"Technician", (string?)Ids.Technician, (string?)"PRO");
        var facade = new Fake<IProfilesContextFacade>().Returns(nameof(IProfilesContextFacade.GetProfileClaimsAsync), claims);
        var sut = new ExternalProfilesService(facade.Object);

        // Act
        var result = await sut.GetProfileClaimsAsync(Ids.User);

        // Assert
        Assert.Equal(claims, result);
    }

    [Fact]
    public async Task ExternalProfilesService_GetProfileClaimsAsyncWithoutProfile_ReturnsNull()
    {
        // Arrange
        var sut = new ExternalProfilesService(new Fake<IProfilesContextFacade>().Object);

        // Act
        var result = await sut.GetProfileClaimsAsync(Ids.User);

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExternalStoreAccessService_CanManage_ReturnsFacadeDecision(bool allowed)
    {
        // Arrange
        var stores = new Fake<IStoreAccessFacade>().Returns(nameof(IStoreAccessFacade.CanManage), allowed);
        var sut = new ExternalStoreAccessService(stores.Object);

        // Act
        var result = await sut.CanManage(Ids.Actor, Ids.Store, CancellationToken.None);

        // Assert
        Assert.Equal(allowed, result);
    }
}
