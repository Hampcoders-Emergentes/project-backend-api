using System.Reflection;
using Hampcoders.Electrolink.API.Assets.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Electrolink.Backend.Characterization.Tests.StoreManagement;
using MediatR;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.Assets;

public sealed class DeviceRetirementPolicyTests
{
    [Theory]
    [InlineData("HomeOwner")]
    [InlineData("Company")]
    public async Task Owner_subject_can_retire_and_repeat_without_another_event(string role)
    {
        var f = new Fixture(role, "valid");
        await f.Service.Handle(new DecommissionOwnedDeviceCommand(f.Actor, f.Device.Id.Value, "Synthetic"));
        Assert.Equal(EDeviceStatus.Decommissioned, f.Device.Status); Assert.Equal(1, f.Events);
        await f.Service.Handle(new DecommissionOwnedDeviceCommand(f.Actor, f.Device.Id.Value, "Repeated"));
        Assert.Equal(1, f.Events);
    }
    [Theory]
    [InlineData("inactive-profile")]
    [InlineData("unassigned")]
    [InlineData("foreign")]
    public async Task Missing_authority_never_saves_or_publishes(string condition)
    {
        var f = new Fixture("HomeOwner", condition); var before = f.Device.Status;
        await Assert.ThrowsAsync<DeviceRetirementUnavailableException>(() => f.Service.Handle(new DecommissionOwnedDeviceCommand(f.Actor, f.Device.Id.Value, "Synthetic")));
        Assert.Equal(before, f.Device.Status); Assert.Equal(0, f.Saves); Assert.Equal(0, f.Events);
    }
    internal sealed class Fixture
    {
        internal UserId Actor { get; } = UserId.From("us-m37-owner");
        internal IoTDevice Device { get; }
        internal IoTDeviceCommandService Service { get; }
        internal Hampcoders.Electrolink.API.Assets.Application.Internal.QueryServices.IoTDeviceQueryService Queries { get; }
        internal int Saves { get; private set; }
        internal int Events { get; private set; }
        internal Fixture(string role, string condition)
        {
            var subject = role == "HomeOwner" ? "ho-m37-owner" : "comp-m37-owner";
            var owner = role == "HomeOwner" ? ClientIdentity.FromHomeowner(subject) : ClientIdentity.FromCompany(subject);
            var property = Property.Create(owner, Address.Create("Synthetic", "1", "Synthetic", "Synthetic", "Synthetic", "00000"), Geolocation.Create(0, 0, null, "SYNTHETIC"));
            Device = IoTDevice.Register(SerialNumber.From("SYNTHETIC-M37"), ApiKeyHash.CreateFromPlainText(Guid.NewGuid().ToString()), "Synthetic");
            if (condition != "unassigned")
            {
                Device.AssignToProperty(property.Id, InstallationRequestId.From("synthetic-installation"));
                Device.RecordInstallation(TechnicianId.From("tech-m37"), property.Id, "Synthetic", DateTime.UtcNow);
            }
            Device.ClearDomainEvents();
            var profiles = Proxy<IProfilesContextFacade>((m, args) => m.Name switch
            {
                nameof(IProfilesContextFacade.GetProfileClaimsAsync) => Task.FromResult<(string ProfileId, string ProfileStatus, string? BusinessRole, string? RoleSubjectId, string? SubscriptionTier)?>(
                    ("prof-m37", condition == "inactive-profile" ? "Deactivated" : "Active", role, subject, null)),
                nameof(IProfilesContextFacade.IsHomeownerActiveAsync) or nameof(IProfilesContextFacade.IsCompanyActiveAsync) => Task.FromResult(true),
                _ => throw new InvalidOperationException("Unexpected profile call")
            });
            var devices = Proxy<IIoTDeviceRepository>((m, args) => m.Name == nameof(IIoTDeviceRepository.FindByIdAsync)
                ? Task.FromResult<IoTDevice?>(Device) : throw new InvalidOperationException("Unexpected device call"));
            var properties = Proxy<IPropertyRepository>((m, args) => m.Name switch
            {
                nameof(IPropertyRepository.FindByIdAndOwnerAsync) => Task.FromResult<Property?>(condition == "foreign" || (ClientIdentity)args![1]! != property.Owner ? null : property),
                nameof(IPropertyRepository.FindByIdAsync) => Task.FromResult<Property?>(property),
                _ => throw new InvalidOperationException("Unexpected property call")
            });
            var unit = Proxy<IUnitOfWork>((m, args) => { Saves++; return Task.CompletedTask; });
            var publisher = Proxy<IPublisher>((m, args) => { Events++; return Task.CompletedTask; });
            Service = new(devices, properties, unit, publisher, profiles);
            Queries = new(devices, properties, profiles);
        }
        private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> call) where T : class
        { var proxy = DispatchProxy.Create<T, DeviceAuthorityDouble>(); ((DeviceAuthorityDouble)(object)proxy).Call = call; return proxy; }
    }
}
