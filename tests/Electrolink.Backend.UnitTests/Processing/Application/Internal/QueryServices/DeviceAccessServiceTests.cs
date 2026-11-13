using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Events;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Processing.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Processing.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Processing.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Processing.Domain.Repositories;
using Hampcoders.Electrolink.API.Processing.Domain.Services;
using Hampcoders.Electrolink.API.Processing.Interfaces.ACL.Services;
using Hampcoders.Electrolink.API.Processing.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using static Electrolink.Backend.UnitTests.Processing.ProcessingTestData;
using IAssetsPort = Hampcoders.Electrolink.API.Processing.Application.Internal.OutboundServices.IAssetsContextFacade;
using ProfilesAcl = Hampcoders.Electrolink.API.Profiles.Interfaces.ACL.IProfilesContextFacade;

namespace Electrolink.Backend.UnitTests.Processing.Application.Internal.QueryServices;

public class DeviceAccessServiceTests
{
    private readonly Fake<ProfilesAcl> _profiles = new();
    private readonly Fake<IDeviceReadingStreamRepository> _streams = new();
    private readonly Fake<IAnomalyRecordRepository> _anomalies = new();
    private readonly Fake<IServiceOperationContextFacade> _services = new();

    private DeviceAccessService CreateSut() => new(_profiles.Object, _streams.Object, _anomalies.Object, _services.Object);

    private void GivenHomeowner(string homeowner = Homeowner) => _profiles
        .Returns(nameof(ProfilesAcl.GetProfileClaimsAsync), ("prof-1", "Active", (string?)"HomeOwner", (string?)homeowner, (string?)null))
        .Returns(nameof(ProfilesAcl.GetProfileIdByHomeownerIdAsync), "prof-1");

    private void GivenTechnician() => _profiles
        .Returns(nameof(ProfilesAcl.GetProfileClaimsAsync), ("prof-1", "Active", (string?)"Technician", (string?)Ids.Technician, (string?)null))
        .Returns(nameof(ProfilesAcl.GetTechnicianIdByUserIdAsync), Ids.Technician);

    [Fact]
    public async Task CanAccessDeviceAsync_ForOwner_ReturnsTrue()
    {
        // Arrange
        GivenHomeowner();
        _streams.Returns(nameof(IDeviceReadingStreamRepository.FindByDeviceIdAsync), Stream());
        var sut = CreateSut();

        // Act
        var result = await sut.CanAccessDeviceAsync(Ids.User, Device);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CanAccessDeviceAsync_ForAnotherHomeowner_ReturnsFalse()
    {
        // Arrange
        GivenHomeowner("ho-2");
        _streams.Returns(nameof(IDeviceReadingStreamRepository.FindByDeviceIdAsync), Stream());
        var sut = CreateSut();

        // Act
        var result = await sut.CanAccessDeviceAsync(Ids.User, Device);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CanAccessDeviceAsync_ForTechnicianWithActiveService_ReturnsTrue()
    {
        // Arrange
        GivenTechnician();
        _streams.Returns(nameof(IDeviceReadingStreamRepository.FindByDeviceIdAsync), Stream());
        _services.Returns(nameof(IServiceOperationContextFacade.HasActiveServiceForPropertyAsync), true);
        var sut = CreateSut();

        // Act
        var result = await sut.CanAccessDeviceAsync(Ids.User, Device);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CanAccessDeviceAsync_ForUnknownDevice_ReturnsFalse()
    {
        // Arrange
        GivenHomeowner();
        var sut = CreateSut();

        // Act
        var result = await sut.CanAccessDeviceAsync(Ids.User, Device);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CanAccessAnomalyAsync_ForOwner_ReturnsTrue()
    {
        // Arrange
        GivenHomeowner();
        _anomalies.Returns(nameof(IAnomalyRecordRepository.FindByIdAsync), Anomaly());
        var sut = CreateSut();

        // Act
        var result = await sut.CanAccessAnomalyAsync(Ids.User, "anm-1");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task FilterVisibleAnomaliesAsync_ForActorWithoutProfile_ReturnsEmpty()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.FilterVisibleAnomaliesAsync(Ids.User, Property.Value, [Anomaly()]);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task FilterVisibleAnomaliesAsync_ForOtherHomeowner_HidesForeignAnomalies()
    {
        // Arrange
        GivenHomeowner("ho-2");
        var sut = CreateSut();

        // Act
        var result = await sut.FilterVisibleAnomaliesAsync(Ids.User, Property.Value, [Anomaly()]);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task FilterVisibleAnomaliesAsync_ForTechnicianWithActiveService_ReturnsAllAnomalies()
    {
        // Arrange
        GivenTechnician();
        _services.Returns(nameof(IServiceOperationContextFacade.HasActiveServiceForPropertyAsync), true);
        var sut = CreateSut();

        // Act
        var result = await sut.FilterVisibleAnomaliesAsync(Ids.User, Property.Value, [Anomaly(), Anomaly(EAnomalyType.VoltageSpike)]);

        // Assert
        Assert.Equal(2, result.Count);
    }
}
