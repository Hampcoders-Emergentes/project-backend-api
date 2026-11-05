using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hampcoders.Electrolink.API.Shared.Infrastructure.BackgroundServices;

public class CleanupOrphanedFilesJob : BackgroundService
{
    private readonly ILogger<CleanupOrphanedFilesJob> _logger;
    private readonly IServiceProvider _serviceProvider;

    public CleanupOrphanedFilesJob(ILogger<CleanupOrphanedFilesJob> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cleanup Orphaned Files Job started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);

            if (stoppingToken.IsCancellationRequested) break;

            await ProcessOrphanedFilesAsync(stoppingToken);
        }

        _logger.LogInformation("Cleanup Orphaned Files Job stopped.");
    }

    private async Task ProcessOrphanedFilesAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOrphanedFileRepository>();
        var fileStorageProvider = scope.ServiceProvider.GetRequiredService<IFileStorageProvider>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var orphanedFiles = await repository.ListPendingAsync(maxRetries: 3);

        foreach (var orphanedFile in orphanedFiles)
        {
            if (stoppingToken.IsCancellationRequested) return;

            try
            {
                var deleted = await fileStorageProvider.DeleteAsync(orphanedFile.ProviderId, orphanedFile.Folder);

                if (deleted)
                {
                    await repository.MarkProcessedAsync(orphanedFile.Id);
                    _logger.LogInformation(
                        "Successfully deleted orphaned file {ProviderId} from folder {Folder}",
                        orphanedFile.ProviderId, orphanedFile.Folder);
                }
                else
                {
                    orphanedFile.IncrementRetry("Delete returned false");
                    _logger.LogWarning(
                        "Delete returned false for orphaned file {ProviderId}",
                        orphanedFile.ProviderId);
                }
            }
            catch (Exception ex)
            {
                orphanedFile.IncrementRetry(ex.Message);
                _logger.LogError(ex,
                    "Failed to delete orphaned file {ProviderId} (retry {RetryCount})",
                    orphanedFile.ProviderId, orphanedFile.RetryCount);
            }
        }

        await unitOfWork.CompleteAsync();
    }
}
