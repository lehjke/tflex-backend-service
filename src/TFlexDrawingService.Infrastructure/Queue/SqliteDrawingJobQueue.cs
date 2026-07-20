using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Infrastructure.Queue;

public sealed class SqliteDrawingJobQueue(
    IDrawingJobRepository repository,
    IOptions<DrawingQueueOptions> options,
    ILogger<SqliteDrawingJobQueue> logger) : IDrawingJobQueue
{
    private readonly SemaphoreSlim _signal = new(0);

    public async Task EnqueueAsync(DrawingJob job, CancellationToken cancellationToken = default)
    {
        await repository.CreateAsync(job, cancellationToken);
        _signal.Release();
        logger.LogInformation("Queued drawing job {JobId}", job.Id);
    }

    public async Task<DrawingJobEnqueueResult> TryEnqueueAsync(
        DrawingJob job,
        int maxActiveJobs,
        int maxActiveJobsPerUser,
        CancellationToken cancellationToken = default)
    {
        var result = await repository.TryCreateAsync(
            job,
            maxActiveJobs,
            maxActiveJobsPerUser,
            cancellationToken);

        if (result == DrawingJobEnqueueResult.Enqueued)
        {
            _signal.Release();
            logger.LogInformation("Queued drawing job {JobId}", job.Id);
        }

        return result;
    }

    public async Task<DrawingJob> DequeueAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await RecoverInterruptedAsync(cancellationToken);

            var leaseToken = Guid.NewGuid().ToString("n");
            var leaseExpiresAt = DateTimeOffset.UtcNow + options.Value.LeaseDuration;
            var job = await repository.TryClaimNextPendingAsync(
                leaseToken,
                leaseExpiresAt,
                cancellationToken);
            if (job is not null)
            {
                logger.LogInformation(
                    "Claimed drawing job {JobId} with lease expiring at {LeaseExpiresAt}.",
                    job.Id,
                    job.LeaseExpiresAt);
                return job;
            }

            await _signal.WaitAsync(options.Value.PollInterval, cancellationToken);
        }
    }

    public async Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken = default)
    {
        var recoveredCount = await repository.RequeueExpiredRunningAsync(
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (recoveredCount > 0)
        {
            _signal.Release(recoveredCount);
            logger.LogWarning(
                "Requeued {RecoveredJobCount} drawing job(s) whose worker lease expired.",
                recoveredCount);
        }

        return recoveredCount;
    }
}
