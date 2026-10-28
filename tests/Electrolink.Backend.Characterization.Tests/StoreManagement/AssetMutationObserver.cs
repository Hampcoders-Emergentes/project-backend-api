using MediatR;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

// Observe concrete Assets notifications, without replacing their real selected handlers.
internal sealed class AssetMutationObserver<T>(Action observed) : INotificationHandler<T> where T : INotification
{
    public Task Handle(T notification, CancellationToken cancellationToken) { observed(); return Task.CompletedTask; }
}
