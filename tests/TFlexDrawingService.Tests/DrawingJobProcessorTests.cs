using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Infrastructure.Automation;
using TFlexDrawingService.Infrastructure.Configuration;
using TFlexDrawingService.Infrastructure.Persistence;
using TFlexDrawingService.Infrastructure.Queue;
using TFlexDrawingService.Infrastructure.Services;
using TFlexDrawingService.Infrastructure.Storage;
using TFlexDrawingService.Tests.Support;

namespace TFlexDrawingService.Tests;

public sealed class DrawingJobProcessorTests
{
    [Fact]
    public async Task ProcessAsync_CopiesTemplateAndKeepsOriginalUnchanged()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        var templateDirectory = Path.Combine(root, "templates");
        Directory.CreateDirectory(templateDirectory);

        var templatePath = Path.Combine(templateDirectory, "template.grb");
        const string originalTemplateContent = "original template";
        await File.WriteAllTextAsync(templatePath, originalTemplateContent);
        var fragmentsDirectory = Path.Combine(templateDirectory, "template");
        Directory.CreateDirectory(fragmentsDirectory);
        await File.WriteAllTextAsync(Path.Combine(fragmentsDirectory, "fragment.grb"), "fragment");

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

        var template = new DrawingTemplate
        {
            Id = "sample",
            Code = "sample",
            Name = "Sample",
            TemplateFilePath = templatePath,
            OutputFormats = ["pdf"],
            Parameters = []
        };

        var job = new DrawingJob
        {
            TemplateId = template.Id,
            OutputFormat = "pdf",
            InputParametersJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["WIDTH"] = 1000,
                ["$Oboznach"] = "L1",
                ["TR"] = 30,
                ["$Address"] = "г. Москва, ул. Мира 21\r\n(Корпус 1)"
            })
        };

        await queue.EnqueueAsync(job);
        var claimedJob = await queue.DequeueAsync(CancellationToken.None);

        var processor = new DrawingJobProcessor(
            new InMemoryTemplateCatalog(template),
            repository,
            new LocalFileStorage(storageOptions),
            new MockTFlexAutomationClient(NullLogger<MockTFlexAutomationClient>.Instance),
            CreateReadyAutomationState(),
            Options.Create(new DrawingQueueOptions
            {
                LeaseDuration = TimeSpan.FromMinutes(1),
                LeaseHeartbeatInterval = TimeSpan.FromSeconds(10)
            }),
            NullLogger<DrawingJobProcessor>.Instance);

        await processor.ProcessAsync(claimedJob);

        var savedJob = await repository.GetAsync(job.Id);

        Assert.NotNull(savedJob);
        Assert.Equal(DrawingJobStatus.Completed, savedJob.Status);
        Assert.Single(savedJob.ResultFiles);
        Assert.True(File.Exists(savedJob.ResultFiles[0].Path));
        Assert.Equal("L1 (30) - г. Москва, ул. Мира 21 (Корпус 1).pdf", savedJob.ResultFiles[0].FileName);
        Assert.EndsWith(savedJob.ResultFiles[0].FileName, savedJob.ResultFiles[0].Path);
        Assert.Equal(originalTemplateContent, await File.ReadAllTextAsync(templatePath));
        Assert.True(File.Exists(Path.Combine(savedJob.WorkingDirectory!, "template.grb")));
        Assert.True(File.Exists(Path.Combine(savedJob.WorkingDirectory!, "template", "fragment.grb")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessAsync_CancelsAtLeaseDeadlineWhenRenewalIsUnavailable(
        bool renewalHangs)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        var templatePath = Path.Combine(root, "templates", "template.grb");
        Directory.CreateDirectory(Path.GetDirectoryName(templatePath)!);
        await File.WriteAllTextAsync(templatePath, "template");

        var storageOptions = Options.Create(new DrawingStorageOptions
        {
            RootPath = Path.Combine(root, "storage"),
            DatabasePath = Path.Combine(root, "storage", "drawings.db")
        });
        var repository = new SqliteDrawingJobRepository(
            storageOptions,
            NullLogger<SqliteDrawingJobRepository>.Instance);
        await repository.InitializeAsync();

        var queueOptions = Options.Create(new DrawingQueueOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(10),
            LeaseDuration = TimeSpan.FromMilliseconds(400),
            LeaseHeartbeatInterval = TimeSpan.FromMilliseconds(40)
        });
        var queue = new SqliteDrawingJobQueue(
            repository,
            queueOptions,
            NullLogger<SqliteDrawingJobQueue>.Instance);
        var job = new DrawingJob
        {
            TemplateId = "sample",
            OutputFormat = "pdf",
            InputParametersJson = "{}"
        };
        await queue.EnqueueAsync(job);
        var claimedJob = await queue.DequeueAsync(CancellationToken.None);

        var template = new DrawingTemplate
        {
            Id = "sample",
            Code = "sample",
            Name = "Sample",
            TemplateFilePath = templatePath,
            OutputFormats = ["pdf"]
        };
        var automation = new CancellationAwareAutomationClient();
        var renewalRepository = new UnavailableRenewalRepository(repository, renewalHangs);
        var processor = new DrawingJobProcessor(
            new InMemoryTemplateCatalog(template),
            renewalRepository,
            new LocalFileStorage(storageOptions),
            automation,
            CreateReadyAutomationState(),
            queueOptions,
            NullLogger<DrawingJobProcessor>.Instance);

        await processor.ProcessAsync(claimedJob);

        Assert.NotNull(automation.CancellationObservedAt);
        Assert.NotNull(claimedJob.LeaseExpiresAt);
        Assert.InRange(
            automation.CancellationObservedAt.Value,
            claimedJob.LeaseExpiresAt.Value,
            claimedJob.LeaseExpiresAt.Value.AddSeconds(1));
        Assert.True(renewalRepository.RenewalAttempts >= 1);
        if (!renewalHangs)
        {
            Assert.True(renewalRepository.RenewalAttempts > 1);
        }

        var savedJob = await repository.GetAsync(job.Id);
        Assert.NotNull(savedJob);
        Assert.Equal(DrawingJobStatus.Running, savedJob.Status);
        Assert.Empty(savedJob.ResultFiles);
    }

    private static TFlexAutomationReadinessState CreateReadyAutomationState()
    {
        var state = new TFlexAutomationReadinessState();
        state.Update(
            TFlexAutomationHealthResult.Pass("ready"),
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1));
        return state;
    }

    private sealed class CancellationAwareAutomationClient : ITFlexAutomationClient
    {
        public DateTimeOffset? CancellationObservedAt { get; private set; }

        public async Task<IReadOnlyList<GeneratedFile>> GenerateAsync(
            TFlexGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return [];
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObservedAt = DateTimeOffset.UtcNow;
                throw;
            }
        }
    }

    private sealed class UnavailableRenewalRepository(
        IDrawingJobRepository inner,
        bool renewalHangs) : IDrawingJobRepository
    {
        private int _renewalAttempts;

        public int RenewalAttempts => Volatile.Read(ref _renewalAttempts);

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            return inner.InitializeAsync(cancellationToken);
        }

        public Task CreateAsync(DrawingJob job, CancellationToken cancellationToken = default)
        {
            return inner.CreateAsync(job, cancellationToken);
        }

        public Task<DrawingJobEnqueueResult> TryCreateAsync(
            DrawingJob job,
            int maxActiveJobs,
            int maxActiveJobsPerUser,
            CancellationToken cancellationToken = default)
        {
            return inner.TryCreateAsync(
                job,
                maxActiveJobs,
                maxActiveJobsPerUser,
                cancellationToken);
        }

        public Task<DrawingJob?> GetAsync(
            string id,
            CancellationToken cancellationToken = default)
        {
            return inner.GetAsync(id, cancellationToken);
        }

        public Task<DrawingJob?> GetAsync(
            string id,
            string ownerUserName,
            CancellationToken cancellationToken = default)
        {
            return inner.GetAsync(id, ownerUserName, cancellationToken);
        }

        public Task<IReadOnlyList<DrawingJob>> ListAsync(
            int take = 50,
            CancellationToken cancellationToken = default)
        {
            return inner.ListAsync(take, cancellationToken);
        }

        public Task<IReadOnlyList<DrawingJob>> ListAsync(
            int take,
            string ownerUserName,
            CancellationToken cancellationToken = default)
        {
            return inner.ListAsync(take, ownerUserName, cancellationToken);
        }

        public Task<int> CountActiveAsync(
            string? ownerUserName = null,
            CancellationToken cancellationToken = default)
        {
            return inner.CountActiveAsync(ownerUserName, cancellationToken);
        }

        public Task<IReadOnlyList<DrawingJob>> ListFinishedBeforeAsync(
            DateTimeOffset finishedBefore,
            int take = 100,
            CancellationToken cancellationToken = default)
        {
            return inner.ListFinishedBeforeAsync(finishedBefore, take, cancellationToken);
        }

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            return inner.DeleteAsync(id, cancellationToken);
        }

        public Task<DrawingJob?> TryClaimNextPendingAsync(
            string leaseToken,
            DateTimeOffset leaseExpiresAt,
            CancellationToken cancellationToken = default)
        {
            return inner.TryClaimNextPendingAsync(leaseToken, leaseExpiresAt, cancellationToken);
        }

        public Task<int> RequeueExpiredRunningAsync(
            DateTimeOffset utcNow,
            CancellationToken cancellationToken = default)
        {
            return inner.RequeueExpiredRunningAsync(utcNow, cancellationToken);
        }

        public Task<bool> RenewLeaseAsync(
            string id,
            string leaseToken,
            DateTimeOffset leaseExpiresAt,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _renewalAttempts);
            return renewalHangs
                ? new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously).Task
                : Task.FromException<bool>(new InvalidOperationException("Database unavailable."));
        }

        public Task<bool> UpdateWorkingDirectoryAsync(
            string id,
            string workingDirectory,
            CancellationToken cancellationToken = default,
            string? leaseToken = null)
        {
            return inner.UpdateWorkingDirectoryAsync(
                id,
                workingDirectory,
                cancellationToken,
                leaseToken);
        }

        public Task<bool> AddGeneratedFileAsync(
            GeneratedFile file,
            CancellationToken cancellationToken = default,
            string? leaseToken = null)
        {
            return inner.AddGeneratedFileAsync(file, cancellationToken, leaseToken);
        }

        public Task<GeneratedFile?> GetGeneratedFileAsync(
            string jobId,
            string fileId,
            CancellationToken cancellationToken = default)
        {
            return inner.GetGeneratedFileAsync(jobId, fileId, cancellationToken);
        }

        public Task<bool> MarkCompletedAsync(
            string id,
            DateTimeOffset finishedAt,
            CancellationToken cancellationToken = default,
            string? leaseToken = null)
        {
            return inner.MarkCompletedAsync(id, finishedAt, cancellationToken, leaseToken);
        }

        public Task<bool> MarkFailedAsync(
            string id,
            string errorMessage,
            DateTimeOffset finishedAt,
            CancellationToken cancellationToken = default,
            string? leaseToken = null)
        {
            return inner.MarkFailedAsync(
                id,
                errorMessage,
                finishedAt,
                cancellationToken,
                leaseToken);
        }
    }
}
