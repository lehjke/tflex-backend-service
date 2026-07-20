using TFlexDrawingService.Core.Models;

namespace TFlexDrawingService.Core.Abstractions;

public interface IDrawingJobRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task CreateAsync(DrawingJob job, CancellationToken cancellationToken = default);

    Task<DrawingJobEnqueueResult> TryCreateAsync(
        DrawingJob job,
        int maxActiveJobs,
        int maxActiveJobsPerUser,
        CancellationToken cancellationToken = default);

    Task<DrawingJob?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<DrawingJob?> GetAsync(string id, string ownerUserName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DrawingJob>> ListAsync(int take = 50, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DrawingJob>> ListAsync(
        int take,
        string ownerUserName,
        CancellationToken cancellationToken = default);

    Task<int> CountActiveAsync(string? ownerUserName = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DrawingJob>> ListFinishedBeforeAsync(
        DateTimeOffset finishedBefore,
        int take = 100,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task<DrawingJob?> TryClaimNextPendingAsync(
        string leaseToken,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken = default);

    Task<int> RequeueExpiredRunningAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);

    Task<bool> RenewLeaseAsync(
        string id,
        string leaseToken,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateWorkingDirectoryAsync(
        string id,
        string workingDirectory,
        CancellationToken cancellationToken = default,
        string? leaseToken = null);

    Task<bool> AddGeneratedFileAsync(
        GeneratedFile file,
        CancellationToken cancellationToken = default,
        string? leaseToken = null);

    Task<GeneratedFile?> GetGeneratedFileAsync(string jobId, string fileId, CancellationToken cancellationToken = default);

    Task<bool> MarkCompletedAsync(
        string id,
        DateTimeOffset finishedAt,
        CancellationToken cancellationToken = default,
        string? leaseToken = null);

    Task<bool> MarkFailedAsync(
        string id,
        string errorMessage,
        DateTimeOffset finishedAt,
        CancellationToken cancellationToken = default,
        string? leaseToken = null);
}
