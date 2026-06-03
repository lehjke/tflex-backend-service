using System.Text.Json;
using Microsoft.Extensions.Logging;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Core.Models;

namespace TFlexDrawingService.Infrastructure.Services;

public sealed class DrawingJobProcessor(
    ITemplateCatalog templateCatalog,
    IDrawingJobRepository repository,
    IFileStorage fileStorage,
    ITFlexAutomationClient automationClient,
    ILogger<DrawingJobProcessor> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task ProcessAsync(DrawingJob job, CancellationToken cancellationToken = default)
    {
        try
        {
            var template = await templateCatalog.GetByIdOrCodeAsync(job.TemplateId, cancellationToken)
                ?? throw new InvalidOperationException($"Template '{job.TemplateId}' was not found.");

            var parameters = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                job.InputParametersJson,
                JsonOptions) ?? new Dictionary<string, object?>();

            var workingDirectory = await fileStorage.CreateWorkingDirectoryAsync(job, cancellationToken);
            await repository.UpdateWorkingDirectoryAsync(job.Id, workingDirectory, cancellationToken);

            var templateCopyPath = await fileStorage.CopyTemplateToWorkingDirectoryAsync(
                template,
                workingDirectory,
                cancellationToken);

            var resultDirectory = fileStorage.CreateGeneratedDirectory(job.Id);
            var generatedFiles = await automationClient.GenerateAsync(
                new TFlexGenerationRequest(
                    job,
                    template,
                    workingDirectory,
                    templateCopyPath,
                    resultDirectory,
                    parameters,
                    job.OutputFormat),
                cancellationToken);

            foreach (var file in generatedFiles)
            {
                await repository.AddGeneratedFileAsync(file, cancellationToken);
            }

            await repository.MarkCompletedAsync(job.Id, DateTimeOffset.UtcNow, cancellationToken);
            logger.LogInformation("Drawing job {JobId} completed with {FileCount} file(s).", job.Id, generatedFiles.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Drawing job {JobId} failed.", job.Id);
            await repository.MarkFailedAsync(job.Id, exception.Message, DateTimeOffset.UtcNow, CancellationToken.None);
        }
    }
}
