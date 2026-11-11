using Hampcoders.Electrolink.API.StoreManagement.Application;

namespace Hampcoders.Electrolink.API.StoreManagement.Interfaces.ACL;

public sealed record OnDutyWorker(string WorkerCode, string WorkerName, string Role);

/// <summary>Public ACL: who was on shift in a store at a given time. Internal callers only (no authorization).</summary>
public interface IStaffRosterFacade
{
    Task<OnDutyWorker?> OnDutyAt(string storeId, DateTime atUtc, CancellationToken ct);
}

public sealed class StaffRosterFacade(ShiftOperationsService shifts) : IStaffRosterFacade
{
    public async Task<OnDutyWorker?> OnDutyAt(string storeId, DateTime atUtc, CancellationToken ct)
    {
        var shift = await shifts.AccountableAt(storeId, atUtc, ct);
        return shift == null ? null : new(shift.WorkerCode, shift.WorkerName, shift.Role);
    }
}
