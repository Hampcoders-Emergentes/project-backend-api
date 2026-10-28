using Hampcoders.Electrolink.API.Assets.Domain.Model.Events;
using MediatR;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

// Observation only: real application services, repositories and handlers remain in use.
internal sealed class RetirementEventWitness : INotificationHandler<DeviceDecommissionedEvent>
{
    internal int Count { get; private set; }
    public Task Handle(DeviceDecommissionedEvent notification, CancellationToken cancellationToken)
    { Count++; return Task.CompletedTask; }
}
