namespace TFlexDrawingService.Core.Models;

public enum DrawingJobEnqueueResult
{
    Enqueued,
    TotalLimitReached,
    UserLimitReached
}
