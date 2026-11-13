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

public class StaffMemberCommandServiceTests
{
    private readonly Fake<IStaffMemberRepository> _staff = new();
    private readonly Fake<IIamContextFacade> _iam = new Fake<IIamContextFacade>().Returns(nameof(IIamContextFacade.UserExistsAsync), true);
    private readonly Fake<IMediator> _mediator = new();

    private StaffMemberCommandService CreateSut() => new(_staff.Object, new ExternalIamService(_iam.Object), new Fake<IUnitOfWork>().Object,
        _mediator.Object, NullLogger<StaffMemberCommandService>.Instance);

    private StaffMember GivenStaff()
    {
        var member = StaffMember.Create(Ids.User, "Ana", "Diaz", "+51999");
        member.ClearDomainEvents();
        _staff.Returns(nameof(IStaffMemberRepository.FindByIdAsync), member);
        return member;
    }

    [Fact]
    public async Task Handle_CreateForUnknownIamUser_ThrowsArgumentException()
    {
        // Arrange
        _iam.Returns(nameof(IIamContextFacade.UserExistsAsync), false);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new CreateStaffMemberCommand(Ids.User, "Ana", "Diaz", null));

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_CreateWhenStaffAlreadyExists_ThrowsInvalidOperationException()
    {
        // Arrange
        _staff.Returns(nameof(IStaffMemberRepository.FindByUserIdAsync), StaffMember.Create(Ids.User, "Ana", "Diaz", null));
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new CreateStaffMemberCommand(Ids.User, "Ana", "Diaz", null));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_CreateForNewUser_PersistsActiveStaffMember()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var member = await sut.Handle(new CreateStaffMemberCommand(Ids.User, "Ana", "Diaz", "+51999"));

        // Assert
        Assert.Equal((true, "Ana"), (member.IsActive, member.FirstName));
        Assert.Same(member, _staff.CallsTo(nameof(IStaffMemberRepository.AddAsync)).Single().Arg<StaffMember>());
    }

    [Fact]
    public async Task Handle_AssignZone_SetsAssignedZone()
    {
        // Arrange
        var member = GivenStaff();
        var sut = CreateSut();

        // Act
        await sut.Handle(new AssignStaffZoneCommand(member.Id.Value, "Lima Sur", -12.2, -76.9, 10));

        // Assert
        Assert.Equal("Lima Sur", member.AssignedZone!.Region);
    }

    [Fact]
    public async Task Handle_AssignZoneForUnknownStaff_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new AssignStaffZoneCommand(StaffMemberId.NewStaffMemberId().Value, "Lima", -12, -77, 10));

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_UpdateDataWithoutPhone_KeepsExistingPhone()
    {
        // Arrange
        var member = GivenStaff();
        var sut = CreateSut();

        // Act
        await sut.Handle(new UpdateStaffMemberDataCommand(member.Id.Value, "Eva", "Rios", null));

        // Assert
        Assert.Equal(("Eva", "Rios", "+51999"), (member.FirstName, member.LastName, member.PhoneNumber));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Handle_DeactivateCommand_SetsActiveFlagFromReactivateOption(bool reactivate, bool expectedActive)
    {
        // Arrange
        var member = GivenStaff();
        member.Deactivate();
        if (!reactivate) member.Reactivate();
        var sut = CreateSut();

        // Act
        await sut.Handle(new DeactivateStaffMemberCommand(member.Id.Value, reactivate));

        // Assert
        Assert.Equal(expectedActive, member.IsActive);
    }

    [Fact]
    public async Task Handle_GrantIoTCertification_CertifiesStaffAndPublishesEvent()
    {
        // Arrange
        var member = GivenStaff();
        var sut = CreateSut();

        // Act
        await sut.Handle(new GrantStaffIoTCertificationCommand(member.Id.Value));

        // Assert
        Assert.True(member.IsIoTCertified);
        Assert.Equal(1, _mediator.CallCount(nameof(IMediator.Publish)));
    }

    [Fact]
    public async Task Handle_RevokeIoTCertificationWhenNotCertified_ThrowsInvalidOperationException()
    {
        // Arrange
        var member = GivenStaff();
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(new RevokeStaffIoTCertificationCommand(member.Id.Value));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Handle_RevokeIoTCertificationWhenCertified_RemovesCertification()
    {
        // Arrange
        var member = GivenStaff();
        member.GrantIoTCertification();
        var sut = CreateSut();

        // Act
        await sut.Handle(new RevokeStaffIoTCertificationCommand(member.Id.Value));

        // Assert
        Assert.False(member.IsIoTCertified);
    }
}
