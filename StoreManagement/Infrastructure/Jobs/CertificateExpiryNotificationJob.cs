using Hampcoders.Electrolink.API.StoreManagement.Application;

namespace Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Jobs;

/// <summary>Checks maintenance certificate expirations every hour and warns the store managers concerned.</summary>
public sealed class CertificateExpiryNotificationJob(IServiceScopeFactory scopes, ILogger<CertificateExpiryNotificationJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var sent = await scope.ServiceProvider.GetRequiredService<CertificateExpiryNotifier>().NotifyDue(DateTime.UtcNow, stoppingToken);
                if (sent > 0) logger.LogInformation("[Compliance] {Count} certificate expiry notice(s) sent", sent);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "[Compliance] Certificate expiry check failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
