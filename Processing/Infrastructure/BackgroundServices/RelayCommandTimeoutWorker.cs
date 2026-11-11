using Hampcoders.Electrolink.API.Processing.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using MediatR;

namespace Hampcoders.Electrolink.API.Processing.Infrastructure.BackgroundServices;

/// <summary>
///     Times out relay commands the device never answered. Polling a persisted state replaces the former
///     in-memory fire-and-forget delay, so a restart or a failure can no longer leave a command stuck in Sent.
/// </summary>
public sealed class RelayCommandTimeoutWorker(IServiceScopeFactory scopes, ILogger<RelayCommandTimeoutWorker> logger) : BackgroundService
{
    private static readonly TimeSpan AckTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SweepAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "[IoT] Relay timeout sweep failed; will retry."); }
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    internal async Task SweepAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRelayControlCommandRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var stale = await repo.FindUnansweredIssuedBeforeAsync(DateTime.UtcNow - AckTimeout, 100);
        foreach (var command in stale)
        {
            command.MarkAsTimeout();
            await repo.UpdateAsync(command);
        }
        if (stale.Count == 0) return;
        await uow.CompleteAsync();
        foreach (var command in stale)
        {
            foreach (var domainEvent in command.DomainEvents) await mediator.Publish(domainEvent, CancellationToken.None);
            command.ClearDomainEvents();
        }
        logger.LogInformation("[IoT] {Count} unanswered relay command(s) timed out.", stale.Count);
    }
}
