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

    public async Task<DrawingJob> DequeueAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var job = await repository.TryClaimNextPendingAsync(cancellationToken);
            if (job is not null)
            {
                logger.LogInformation("Claimed drawing job {JobId}", job.Id);
                return job;
            }

            await _signal.WaitAsync(options.Value.PollInterval, cancellationToken);
        }
    }
}
