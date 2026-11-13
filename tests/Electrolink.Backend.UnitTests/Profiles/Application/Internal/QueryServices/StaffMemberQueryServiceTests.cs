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

namespace Electrolink.Backend.UnitTests.Profiles.Application.Internal.QueryServices;

public class StaffMemberQueryServiceTests
{
    private readonly Fake<IStaffMemberRepository> _staff = new();

    [Fact]
    public async Task Handle_GetById_ReturnsStaffMember()
    {
        // Arrange
        var member = StaffMember.Create(Ids.User, "Ana", "Diaz", null);
        _staff.Returns(nameof(IStaffMemberRepository.FindByIdAsync), member);
        var sut = new StaffMemberQueryService(_staff.Object);

        // Act
        var result = await sut.Handle(new GetStaffMemberByIdQuery(member.Id.Value));

        // Assert
        Assert.Same(member, result);
    }

    [Fact]
    public async Task Handle_GetAll_ReturnsEveryStaffMember()
    {
        // Arrange
        _staff.Returns(nameof(IStaffMemberRepository.ListAsync), new[] { StaffMember.Create(Ids.User, "A", "B", null) });
        var sut = new StaffMemberQueryService(_staff.Object);

        // Act
        var result = await sut.Handle(new GetAllStaffMembersQuery());

        // Assert
        Assert.Single(result);
    }

    [Fact]
    public async Task Handle_GetAvailableMakersInZone_QueriesCertifiedStaffByLocation()
    {
        // Arrange
        var sut = new StaffMemberQueryService(_staff.Object);

        // Act
        await sut.Handle(new GetAvailableMakersInZoneQuery(-12, -77));

        // Assert
        Assert.Equal(new object?[] { -12d, -77d }, _staff.Calls.Single().Args);
    }

    [Fact]
    public async Task Handle_GetByUserIdWhenMissing_ReturnsNull()
    {
        // Arrange
        var sut = new StaffMemberQueryService(_staff.Object);

        // Act
        var result = await sut.Handle(new GetStaffMemberByUserIdQuery(Ids.User));

        // Assert
        Assert.Null(result);
    }
}
