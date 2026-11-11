using Hampcoders.Electrolink.API.AlertManagement.Application;
using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.StoreManagement.Interfaces.ACL;

namespace Hampcoders.Electrolink.API.AlertManagement.Infrastructure;

/// <summary>Anti-corruption adapter: reads the store's shift roster through the StoreManagement ACL.</summary>
public sealed class OnDutyWorkerAdapter(IStaffRosterFacade roster) : IOnDutyWorkerPort
{
    public async Task<WorkerAttribution?> OnDutyAt(string storeId, DateTime atUtc, CancellationToken ct)
    {
        var worker = await roster.OnDutyAt(storeId, atUtc, ct);
        return worker == null ? null : new WorkerAttribution(worker.WorkerCode, worker.WorkerName);
    }
}
