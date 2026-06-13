using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Infrastructure.Services;

public sealed class StorageCleanupHostedService(
    IDrawingJobRepository repository,
    IOptions<DrawingCleanupOptions> cleanupOptions,
    IOptions<DrawingStorageOptions> storageOptions,
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
            var generatedRoot = GetStorageSubdirectory("generated");
            var jobsRoot = GetStorageSubdirectory("jobs");
            var jobs = await repository.ListFinishedBeforeAsync(
                cutoff,
                options.BatchSize,
                cancellationToken);

            foreach (var job in jobs)
            {
                foreach (var file in job.ResultFiles)
                {
                    DeleteFileIfExistsUnderRoot(file.Path, generatedRoot);
                    DeleteDirectoryIfEmptyUnderRoot(Path.GetDirectoryName(file.Path), generatedRoot);
                }

                DeleteDirectoryIfExistsUnderRoot(job.WorkingDirectory, jobsRoot);
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

    private string GetStorageSubdirectory(string directoryName)
    {
        return Path.GetFullPath(Path.Combine(storageOptions.Value.RootPath, directoryName));
    }

    private void DeleteFileIfExistsUnderRoot(string? path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        if (!IsPathUnderRoot(path, root))
        {
            logger.LogWarning("Skipped cleanup of file outside storage root: {Path}", path);
            return;
        }

        File.Delete(path);
    }

    private void DeleteDirectoryIfExistsUnderRoot(string? path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        if (!IsPathUnderRoot(path, root))
        {
            logger.LogWarning("Skipped cleanup of directory outside storage root: {Path}", path);
            return;
        }

        Directory.Delete(path, recursive: true);
    }

    private void DeleteDirectoryIfEmptyUnderRoot(string? path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        if (!IsPathUnderRoot(path, root))
        {
            logger.LogWarning("Skipped cleanup of directory outside storage root: {Path}", path);
            return;
        }

        if (!Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path, recursive: false);
        }
    }

    private static bool IsPathUnderRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedRoot = Path.GetFullPath(root);
        if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            normalizedRoot += Path.DirectorySeparatorChar;
        }

        return Path.GetFullPath(path).StartsWith(normalizedRoot, comparison);
    }
}
