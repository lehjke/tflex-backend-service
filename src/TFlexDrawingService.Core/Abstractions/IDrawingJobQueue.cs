using TFlexDrawingService.Core.Models;

namespace TFlexDrawingService.Core.Abstractions;

public interface IDrawingJobQueue
{
    Task EnqueueAsync(DrawingJob job, CancellationToken cancellationToken = default);

    Task<DrawingJob> DequeueAsync(CancellationToken cancellationToken);
}
