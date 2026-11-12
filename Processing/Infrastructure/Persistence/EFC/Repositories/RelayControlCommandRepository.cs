using Hampcoders.Electrolink.API.Processing.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Processing.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hampcoders.Electrolink.API.Processing.Infrastructure.Persistence.EFC.Repositories;

public class RelayControlCommandRepository(AppDbContext context)
    : BaseRepository<RelayControlCommand, RelayCommandId>(context), IRelayControlCommandRepository
{
    public async Task<RelayControlCommand?> FindPendingByDeviceAsync(string deviceId)
        => await Context.Set<RelayControlCommand>()
            .FirstOrDefaultAsync(c =>
                c.DeviceId == DeviceId.From(deviceId) &&
                (c.Status == ERelayCommandStatus.Pending || c.Status == ERelayCommandStatus.Sent));

    public async Task<IEnumerable<RelayControlCommand>> FindHistoryByDeviceAsync(string deviceId)
        => await Context.Set<RelayControlCommand>()
            .Where(c => c.DeviceId == DeviceId.From(deviceId))
            .OrderByDescending(c => c.IssuedAt)
            .AsNoTracking()
            .ToListAsync();

    public async Task<RelayControlCommand?> FindByCommandIdAsync(string commandId)
        => await Context.Set<RelayControlCommand>()
            .FirstOrDefaultAsync(c => c.CommandId == RelayCommandId.From(commandId));

    public async Task<IReadOnlyList<RelayControlCommand>> FindUnansweredIssuedBeforeAsync(DateTime issuedBeforeUtc, int take)
        => await Context.Set<RelayControlCommand>()
            .Where(c => (c.Status == ERelayCommandStatus.Sent || c.Status == ERelayCommandStatus.Pending) && c.IssuedAt < issuedBeforeUtc)
            .OrderBy(c => c.IssuedAt).Take(take).ToListAsync();

    public async Task UpdateAsync(RelayControlCommand cmd)
        => Context.Set<RelayControlCommand>().Update(cmd);
}
