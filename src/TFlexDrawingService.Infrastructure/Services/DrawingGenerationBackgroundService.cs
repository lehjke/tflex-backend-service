using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TFlexDrawingService.Core.Abstractions;

namespace TFlexDrawingService.Infrastructure.Services;

public sealed class DrawingGenerationBackgroundService(
    IDrawingJobQueue queue,
    DrawingJobProcessor processor,
    ILogger<DrawingGenerationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("T-FLEX generation worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await queue.DequeueAsync(stoppingToken);
                await processor.ProcessAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unhandled error in drawing generation loop.");
            }
        }
    }
}
