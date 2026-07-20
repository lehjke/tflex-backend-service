using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Infrastructure.Configuration;
using TFlexDrawingService.Infrastructure.Persistence;
using TFlexDrawingService.Infrastructure.Queue;
using TFlexDrawingService.Infrastructure.Storage;

namespace TFlexDrawingService.Tests;

public sealed class DrawingJobQueueTests
{
    [Fact]
    public async Task SqliteNativeLibrary_IsPatchedVersion()
    {
        var (_, _, storageOptions) = await CreateQueueAsync();
        await using var connection = new SqliteConnection($"Data Source={storageOptions.Value.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";

        var versionText = Assert.IsType<string>(await command.ExecuteScalarAsync());
        var version = Version.Parse(versionText);

        Assert.True(
            version >= new Version(3, 50, 2),
            $"Expected SQLite 3.50.2 or newer, but loaded {version}.");
    }

    [Fact]
    public async Task TryEnqueueAsync_EnforcesTotalLimitAtomically()
    {
        var (repository, queue, _) = await CreateQueueAsync();
        var attempts = Enumerable.Range(0, 12)
            .Select(index => queue.TryEnqueueAsync(
                CreateJob($"owner-{index}"),
                maxActiveJobs: 5,
                maxActiveJobsPerUser: 5))
            .ToArray();

        var results = await Task.WhenAll(attempts);

        Assert.Equal(5, results.Count(result => result == DrawingJobEnqueueResult.Enqueued));
        Assert.Equal(7, results.Count(result => result == DrawingJobEnqueueResult.TotalLimitReached));
        Assert.Equal(5, await repository.CountActiveAsync());
    }

    [Fact]
    public async Task TryEnqueueAsync_EnforcesPerUserLimitAtomically()
    {
        var (repository, queue, _) = await CreateQueueAsync();
        var attempts = Enumerable.Range(0, 8)
            .Select(_ => queue.TryEnqueueAsync(
                CreateJob("operator"),
                maxActiveJobs: 20,
                maxActiveJobsPerUser: 3))
            .ToArray();

        var results = await Task.WhenAll(attempts);

        Assert.Equal(3, results.Count(result => result == DrawingJobEnqueueResult.Enqueued));
        Assert.Equal(5, results.Count(result => result == DrawingJobEnqueueResult.UserLimitReached));
        Assert.Equal(3, await repository.CountActiveAsync("operator"));
    }

    [Fact]
    public async Task RecoverInterruptedAsync_DoesNotRequeueActiveLeaseAndRecoversExpiredLease()
    {
        var (repository, queue, storageOptions) = await CreateQueueAsync();
        var job = CreateJob("operator");
        await queue.EnqueueAsync(job);

        var claimed = await queue.DequeueAsync(CancellationToken.None);
        Assert.Equal(DrawingJobStatus.Running, claimed.Status);

        var generatedDirectory = Path.Combine(storageOptions.Value.RootPath, "generated", job.Id);
        Directory.CreateDirectory(generatedDirectory);
        var partialPath = Path.Combine(generatedDirectory, "partial.pdf");
        await File.WriteAllTextAsync(partialPath, "partial");
        await repository.AddGeneratedFileAsync(new GeneratedFile
        {
            JobId = job.Id,
            FileName = "partial.pdf",
            Format = "pdf",
            Path = partialPath,
            SizeBytes = new FileInfo(partialPath).Length
        });

        var secondQueue = new SqliteDrawingJobQueue(
            repository,
            Options.Create(new DrawingQueueOptions
            {
                PollInterval = TimeSpan.FromMilliseconds(10),
                LeaseDuration = TimeSpan.FromMinutes(1)
            }),
            NullLogger<SqliteDrawingJobQueue>.Instance);
        Assert.Equal(0, await secondQueue.RecoverInterruptedAsync());
        Assert.Equal(DrawingJobStatus.Running, (await repository.GetAsync(job.Id))!.Status);

        Assert.Equal(
            1,
            await repository.RequeueExpiredRunningAsync(DateTimeOffset.UtcNow.AddMinutes(2)));

        var recovered = await repository.GetAsync(job.Id);
        Assert.NotNull(recovered);
        Assert.Equal(DrawingJobStatus.Pending, recovered.Status);
        Assert.Null(recovered.StartedAt);
        Assert.Empty(recovered.ResultFiles);

        var reclaimed = await secondQueue.DequeueAsync(CancellationToken.None);
        Assert.Equal(job.Id, reclaimed.Id);
        Assert.Equal(DrawingJobStatus.Running, reclaimed.Status);
        Assert.NotEqual(claimed.LeaseToken, reclaimed.LeaseToken);
    }

    [Fact]
    public async Task ExpiredAttempt_CannotPublishFilesOrOverwriteNewAttemptStatus()
    {
        var (repository, firstQueue, storageOptions) = await CreateQueueAsync();
        var job = CreateJob("operator");
        await firstQueue.EnqueueAsync(job);
        var firstAttempt = await firstQueue.DequeueAsync(CancellationToken.None);

        Assert.Equal(
            1,
            await repository.RequeueExpiredRunningAsync(DateTimeOffset.UtcNow.AddMinutes(2)));

        var secondQueue = new SqliteDrawingJobQueue(
            repository,
            Options.Create(new DrawingQueueOptions
            {
                PollInterval = TimeSpan.FromMilliseconds(10),
                LeaseDuration = TimeSpan.FromMinutes(1)
            }),
            NullLogger<SqliteDrawingJobQueue>.Instance);
        var secondAttempt = await secondQueue.DequeueAsync(CancellationToken.None);
        Assert.NotEqual(firstAttempt.LeaseToken, secondAttempt.LeaseToken);

        var stalePath = Path.Combine(storageOptions.Value.RootPath, "stale.pdf");
        Directory.CreateDirectory(storageOptions.Value.RootPath);
        await File.WriteAllTextAsync(stalePath, "stale");
        var staleFile = new GeneratedFile
        {
            JobId = job.Id,
            FileName = "stale.pdf",
            Format = "pdf",
            Path = stalePath,
            SizeBytes = new FileInfo(stalePath).Length
        };

        Assert.False(await repository.AddGeneratedFileAsync(
            staleFile,
            leaseToken: firstAttempt.LeaseToken));
        Assert.False(await repository.MarkCompletedAsync(
            job.Id,
            DateTimeOffset.UtcNow,
            leaseToken: firstAttempt.LeaseToken));

        var stillOwnedBySecondAttempt = await repository.GetAsync(job.Id);
        Assert.NotNull(stillOwnedBySecondAttempt);
        Assert.Equal(DrawingJobStatus.Running, stillOwnedBySecondAttempt.Status);
        Assert.Equal(secondAttempt.LeaseToken, stillOwnedBySecondAttempt.LeaseToken);
        Assert.Empty(stillOwnedBySecondAttempt.ResultFiles);

        Assert.True(await repository.MarkCompletedAsync(
            job.Id,
            DateTimeOffset.UtcNow,
            leaseToken: secondAttempt.LeaseToken));
    }

    [Fact]
    public async Task RenewLeaseAsync_PreventsRecoveryAtOriginalExpiry()
    {
        var (repository, queue, _) = await CreateQueueAsync();
        var job = CreateJob("operator");
        await queue.EnqueueAsync(job);
        var attempt = await queue.DequeueAsync(CancellationToken.None);
        Assert.NotNull(attempt.LeaseExpiresAt);

        var renewedUntil = DateTimeOffset.UtcNow.AddMinutes(10);
        Assert.True(await repository.RenewLeaseAsync(
            job.Id,
            attempt.LeaseToken!,
            renewedUntil));
        Assert.Equal(
            0,
            await repository.RequeueExpiredRunningAsync(attempt.LeaseExpiresAt!.Value.AddSeconds(1)));

        var running = await repository.GetAsync(job.Id);
        Assert.NotNull(running);
        Assert.Equal(DrawingJobStatus.Running, running.Status);
        Assert.Equal(renewedUntil, running.LeaseExpiresAt);
    }

    [Fact]
    public async Task LocalFileStorage_UsesIsolatedDirectoriesForEachAttempt()
    {
        var (_, _, storageOptions) = await CreateQueueAsync();
        var storage = new LocalFileStorage(storageOptions);
        var firstAttempt = CreateJob("operator");
        firstAttempt.LeaseToken = Guid.NewGuid().ToString("n");

        var firstWorkingDirectory = await storage.CreateWorkingDirectoryAsync(firstAttempt);
        var firstGeneratedDirectory = storage.CreateGeneratedDirectory(firstAttempt);
        var staleWorkingFile = Path.Combine(firstWorkingDirectory, "stale.tmp");
        var staleGeneratedFile = Path.Combine(firstGeneratedDirectory, "partial.pdf");
        await File.WriteAllTextAsync(staleWorkingFile, "stale");
        await File.WriteAllTextAsync(staleGeneratedFile, "partial");

        var secondAttempt = CreateJob("operator");
        secondAttempt.Id = firstAttempt.Id;
        secondAttempt.LeaseToken = Guid.NewGuid().ToString("n");
        var secondWorkingDirectory = await storage.CreateWorkingDirectoryAsync(secondAttempt);
        var secondGeneratedDirectory = storage.CreateGeneratedDirectory(secondAttempt);

        Assert.NotEqual(firstWorkingDirectory, secondWorkingDirectory);
        Assert.NotEqual(firstGeneratedDirectory, secondGeneratedDirectory);
        Assert.True(File.Exists(staleWorkingFile));
        Assert.True(File.Exists(staleGeneratedFile));
        Assert.Empty(Directory.EnumerateFileSystemEntries(secondWorkingDirectory));
        Assert.Empty(Directory.EnumerateFileSystemEntries(secondGeneratedDirectory));

        await File.WriteAllTextAsync(Path.Combine(secondWorkingDirectory, "retry.tmp"), "retry");
        await File.WriteAllTextAsync(Path.Combine(secondGeneratedDirectory, "retry.pdf"), "retry");
        Assert.Equal(secondWorkingDirectory, await storage.CreateWorkingDirectoryAsync(secondAttempt));
        Assert.Equal(secondGeneratedDirectory, storage.CreateGeneratedDirectory(secondAttempt));
        Assert.Empty(Directory.EnumerateFileSystemEntries(secondWorkingDirectory));
        Assert.Empty(Directory.EnumerateFileSystemEntries(secondGeneratedDirectory));
        Assert.True(File.Exists(staleWorkingFile));
        Assert.True(File.Exists(staleGeneratedFile));
    }

    private static async Task<(
        SqliteDrawingJobRepository Repository,
        SqliteDrawingJobQueue Queue,
        IOptions<DrawingStorageOptions> StorageOptions)> CreateQueueAsync()
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

        var queue = new SqliteDrawingJobQueue(
            repository,
            Options.Create(new DrawingQueueOptions
            {
                PollInterval = TimeSpan.FromMilliseconds(10),
                LeaseDuration = TimeSpan.FromMinutes(1),
                LeaseHeartbeatInterval = TimeSpan.FromSeconds(10)
            }),
            NullLogger<SqliteDrawingJobQueue>.Instance);
        return (repository, queue, storageOptions);
    }

    private static DrawingJob CreateJob(string ownerUserName)
    {
        return new DrawingJob
        {
            TemplateId = "template",
            OwnerUserName = ownerUserName,
            InputParametersJson = "{}",
            OutputFormat = "pdf"
        };
    }
}
