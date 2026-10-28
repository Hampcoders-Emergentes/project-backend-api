using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

// Test-only coordination: both requests must read free stock before either is allowed to save.
internal sealed class AssignmentRaceGate : SaveChangesInterceptor
{
    private readonly TaskCompletionSource<bool> both = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> releaseLoser = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? deviceId;
    private string? preferredProperty;
    private int armed;
    private int arrivals;
    internal int Arrivals => Volatile.Read(ref arrivals);
    internal void Arm(string device, string property) { deviceId = device; preferredProperty = property; Volatile.Write(ref armed, 1); }
    internal void ReleaseLoser() => releaseLoser.TrySetResult(true);
    internal void Disarm() { Volatile.Write(ref armed, 0); ReleaseLoser(); }
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref armed) == 0 || eventData.Context == null) return result;
        var entry = eventData.Context.ChangeTracker.Entries<IoTDevice>().SingleOrDefault(e => e.Entity.Id.Value == deviceId
            && e.State == EntityState.Modified && e.Entity.Status == EDeviceStatus.Assigned);
        if (entry == null) return result;
        Assert.Equal(EDeviceStatus.InStock, entry.Property(e => e.Status).OriginalValue);
        var number = Interlocked.Increment(ref arrivals); Assert.InRange(number, 1, 2);
        if (number == 2) both.TrySetResult(true);
        await both.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        if (entry.Entity.AssignedPropertyId!.Value != preferredProperty)
            await releaseLoser.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        return result;
    }
}
