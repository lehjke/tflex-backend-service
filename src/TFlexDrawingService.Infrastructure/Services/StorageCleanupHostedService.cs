using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Infrastructure.Services;

public sealed class StorageCleanupHostedService(
    IDrawingJobRepository repository,
    IOptions<DrawingCleanupOptions> cleanupOptions,
    ILogger<StorageCleanupHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = cleanupOptions.Value;
        if (!options.Enabled)
        {
            logger.LogInformation("Storage cleanup is disabled.");
            return;
        }

        await CleanupOnceAsync(options, stoppingToken);

        using var timer = new PeriodicTimer(options.Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CleanupOnceAsync(options, stoppingToken);
        }
    }

    private async Task CleanupOnceAsync(
        DrawingCleanupOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-options.FinishedJobRetentionDays);
            var jobs = await repository.ListFinishedBeforeAsync(
                cutoff,
                options.BatchSize,
                cancellationToken);

            foreach (var job in jobs)
            {
                foreach (var file in job.ResultFiles)
                {
                    DeleteFileIfExists(file.Path);
                    DeleteDirectoryIfEmpty(Path.GetDirectoryName(file.Path));
                }

                DeleteDirectoryIfExists(job.WorkingDirectory);
                await repository.DeleteAsync(job.Id, cancellationToken);
            }

            if (jobs.Count > 0)
            {
                logger.LogInformation("Cleaned up {JobCount} old drawing job(s).", jobs.Count);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Storage cleanup failed.");
        }
    }

    private static void DeleteFileIfExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        File.Delete(path);
    }

    private static void DeleteDirectoryIfExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        Directory.Delete(path, recursive: true);
    }

    private static void DeleteDirectoryIfEmpty(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        if (!Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path, recursive: false);
        }
    }
}
