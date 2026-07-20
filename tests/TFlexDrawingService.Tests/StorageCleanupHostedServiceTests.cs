using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Infrastructure.Configuration;
using TFlexDrawingService.Infrastructure.Persistence;
using TFlexDrawingService.Infrastructure.Services;

namespace TFlexDrawingService.Tests;

public sealed class StorageCleanupHostedServiceTests
{
    [Fact]
    public async Task Cleanup_RemovesUnregisteredFilesFromFailedJobDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        var storageOptions = Options.Create(new DrawingStorageOptions
        {
            RootPath = Path.Combine(root, "storage"),
            DatabasePath = Path.Combine(root, "storage", "drawings.db")
        });
        var repository = new SqliteDrawingJobRepository(
            storageOptions,
            NullLogger<SqliteDrawingJobRepository>.Instance);
        await repository.InitializeAsync();

        var job = new DrawingJob
        {
            TemplateId = "template",
            OwnerUserName = "operator"
        };
        await repository.CreateAsync(job);

        var workingDirectory = Path.Combine(storageOptions.Value.RootPath, "jobs", job.Id);
        var generatedDirectory = Path.Combine(storageOptions.Value.RootPath, "generated", job.Id);
        Directory.CreateDirectory(workingDirectory);
        Directory.CreateDirectory(generatedDirectory);
        await File.WriteAllTextAsync(Path.Combine(workingDirectory, "request.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(generatedDirectory, "unregistered-partial.pdf"), "partial");
        await repository.UpdateWorkingDirectoryAsync(job.Id, workingDirectory);
        await repository.MarkFailedAsync(job.Id, "runner failed", DateTimeOffset.UtcNow.AddDays(-2));

        var service = new StorageCleanupHostedService(
            repository,
            Options.Create(new DrawingCleanupOptions
            {
                Enabled = true,
                FinishedJobRetentionDays = 1,
                BatchSize = 10,
                Interval = TimeSpan.FromHours(24)
            }),
            storageOptions,
            NullLogger<StorageCleanupHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(
                async () => await repository.GetAsync(job.Id) is null,
                TimeSpan.FromSeconds(5));

            Assert.Null(await repository.GetAsync(job.Id));
            Assert.False(Directory.Exists(generatedDirectory));
            Assert.False(Directory.Exists(workingDirectory));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!await condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
    }
}
