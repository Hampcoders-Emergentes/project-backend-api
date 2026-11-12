using Hampcoders.Electrolink.API.Processing.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;

namespace Hampcoders.Electrolink.API.Processing.Domain.Repositories;

public interface IRelayControlCommandRepository : IBaseRepository<RelayControlCommand, RelayCommandId>
{
    Task<RelayControlCommand?> FindPendingByDeviceAsync(string deviceId);
    Task<IEnumerable<RelayControlCommand>> FindHistoryByDeviceAsync(string deviceId);
    Task<RelayControlCommand?> FindByCommandIdAsync(string commandId);
    Task UpdateAsync(RelayControlCommand cmd);
    /// <summary>Pending/Sent commands issued before <paramref name="issuedBeforeUtc"/> that nobody answered.</summary>
    Task<IReadOnlyList<RelayControlCommand>> FindUnansweredIssuedBeforeAsync(DateTime issuedBeforeUtc, int take);
}
