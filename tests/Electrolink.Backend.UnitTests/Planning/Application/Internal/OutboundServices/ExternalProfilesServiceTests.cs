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

public class PlanningExternalProfilesServiceTests
{
    [Fact]
    public async Task GetActiveClientByUserIdAsync_ForConcordantHomeowner_ReturnsHomeownerClient()
    {
        // Arrange
        var profiles = new Fake<IProfilesContextFacade>()
            .Returns(nameof(IProfilesContextFacade.GetProfileClaimsAsync), Claims("HomeOwner", Homeowner))
            .Returns(nameof(IProfilesContextFacade.GetProfileIdByHomeownerIdAsync), "prof-1");
        var sut = new ExternalProfilesService(profiles.Object);

        // Act
        var result = await sut.GetActiveClientByUserIdAsync(Ids.User);

        // Assert
        Assert.Equal(Client, result);
    }

    [Fact]
    public async Task GetActiveClientByUserIdAsync_ForInactiveProfile_ReturnsNull()
    {
        // Arrange
        var profiles = new Fake<IProfilesContextFacade>()
            .Returns(nameof(IProfilesContextFacade.GetProfileClaimsAsync), Claims("HomeOwner", Homeowner, status: "Pending"));
        var sut = new ExternalProfilesService(profiles.Object);

        // Act
        var result = await sut.GetActiveClientByUserIdAsync(Ids.User);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetActiveCompanyClientByUserIdAsync_ForCompany_ReturnsCompanyClient()
    {
        // Arrange
        var sut = new ExternalProfilesService(CompanyProfiles().Object);

        // Act
        var result = await sut.GetActiveCompanyClientByUserIdAsync(Ids.User);

        // Assert
        Assert.Equal(CompanyClient, result);
    }

    [Fact]
    public async Task GetActiveCompanyClientByUserIdAsync_ForHomeowner_ReturnsNull()
    {
        // Arrange
        var profiles = new Fake<IProfilesContextFacade>()
            .Returns(nameof(IProfilesContextFacade.GetProfileClaimsAsync), Claims("HomeOwner", Homeowner))
            .Returns(nameof(IProfilesContextFacade.GetProfileIdByHomeownerIdAsync), "prof-1");
        var sut = new ExternalProfilesService(profiles.Object);

        // Act
        var result = await sut.GetActiveCompanyClientByUserIdAsync(Ids.User);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetActiveTechnicianByUserIdAsync_ForLinkedTechnician_ReturnsTechnicianAndProfile()
    {
        // Arrange
        var profiles = new Fake<IProfilesContextFacade>()
            .Returns(nameof(IProfilesContextFacade.GetProfileClaimsAsync), Claims("Technician", Ids.Technician))
            .Returns(nameof(IProfilesContextFacade.GetTechnicianIdByUserIdAsync), Ids.Technician);
        var sut = new ExternalProfilesService(profiles.Object);

        // Act
        var result = await sut.GetActiveTechnicianByUserIdAsync(Ids.User);

        // Assert
        Assert.Equal((Ids.Technician, "prof-1"), result);
    }

    [Fact]
    public async Task GetActiveTechnicianByUserIdAsync_WhenLinkDiffers_ReturnsNull()
    {
        // Arrange
        var profiles = new Fake<IProfilesContextFacade>()
            .Returns(nameof(IProfilesContextFacade.GetProfileClaimsAsync), Claims("Technician", Ids.Technician));
        var sut = new ExternalProfilesService(profiles.Object);

        // Act
        var result = await sut.GetActiveTechnicianByUserIdAsync(Ids.User);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task IsClientActiveAsync_ForCompany_UsesCompanyStatus()
    {
        // Arrange
        var sut = new ExternalProfilesService(CompanyProfiles().Object);

        // Act
        var result = await sut.IsClientActiveAsync(CompanyClient);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task EnsureClientIsActiveAsync_ForInactiveClient_ThrowsInactiveHomeownerException()
    {
        // Arrange
        var sut = new ExternalProfilesService(new Fake<IProfilesContextFacade>().Object);

        // Act
        var act = () => sut.EnsureClientIsActiveAsync(Client);

        // Assert
        await Assert.ThrowsAsync<InactiveHomeownerException>(act);
    }

    [Fact]
    public async Task EnsureHomeownerIsActiveAsync_ForInactiveHomeowner_ThrowsInactiveHomeownerException()
    {
        // Arrange
        var sut = new ExternalProfilesService(new Fake<IProfilesContextFacade>().Object);

        // Act
        var act = () => sut.EnsureHomeownerIsActiveAsync(Homeowner);

        // Assert
        await Assert.ThrowsAsync<InactiveHomeownerException>(act);
    }

    [Fact]
    public async Task FindAvailableMakersInZoneAsync_ForwardsIoTRequirement()
    {
        // Arrange
        var profiles = new Fake<IProfilesContextFacade>();
        var sut = new ExternalProfilesService(profiles.Object);

        // Act
        await sut.FindAvailableMakersInZoneAsync(-12, -77, requireIoTCertification: true);

        // Assert
        Assert.Equal(new object?[] { -12d, -77d, true }, profiles.Calls.Single().Args);
    }
}
