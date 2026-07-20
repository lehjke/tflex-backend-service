using TFlexDrawingService.Core.Models;

namespace TFlexDrawingService.Core.Abstractions;

public interface IDrawingJobQueue
{
    Task EnqueueAsync(DrawingJob job, CancellationToken cancellationToken = default);

    Task<DrawingJobEnqueueResult> TryEnqueueAsync(
        DrawingJob job,
        int maxActiveJobs,
        int maxActiveJobsPerUser,
        CancellationToken cancellationToken = default);

    Task<DrawingJob> DequeueAsync(CancellationToken cancellationToken);

    Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken = default);
}
