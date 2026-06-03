using TFlexDrawingService.Core.Models;

namespace TFlexDrawingService.Core.Abstractions;

public interface IDrawingJobRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task CreateAsync(DrawingJob job, CancellationToken cancellationToken = default);

    Task<DrawingJob?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DrawingJob>> ListAsync(int take = 50, CancellationToken cancellationToken = default);

    Task<DrawingJob?> TryClaimNextPendingAsync(CancellationToken cancellationToken = default);

    Task UpdateWorkingDirectoryAsync(string id, string workingDirectory, CancellationToken cancellationToken = default);

    Task AddGeneratedFileAsync(GeneratedFile file, CancellationToken cancellationToken = default);

    Task<GeneratedFile?> GetGeneratedFileAsync(string jobId, string fileId, CancellationToken cancellationToken = default);

    Task MarkCompletedAsync(string id, DateTimeOffset finishedAt, CancellationToken cancellationToken = default);

    Task MarkFailedAsync(string id, string errorMessage, DateTimeOffset finishedAt, CancellationToken cancellationToken = default);
}
