using TFlexDrawingService.Core.Models;

namespace TFlexDrawingService.Core.Abstractions;

public interface IFileStorage
{
    Task<string> CreateWorkingDirectoryAsync(DrawingJob job, CancellationToken cancellationToken = default);

    Task<string> CopyTemplateToWorkingDirectoryAsync(DrawingTemplate template, string workingDirectory, CancellationToken cancellationToken = default);

    string CreateGeneratedDirectory(DrawingJob job);
}
