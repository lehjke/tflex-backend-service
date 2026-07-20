using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Infrastructure.Automation;

namespace TFlexDrawingService.Infrastructure.Services;

public sealed class DrawingGenerationBackgroundService(
    IDrawingJobQueue queue,
    DrawingJobProcessor processor,
    TFlexAutomationReadinessState automationReadiness,
    ILogger<DrawingGenerationBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan ErrorRetryDelay = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("T-FLEX generation worker started.");
        var interruptedJobsRecovered = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!interruptedJobsRecovered)
                {
                    await queue.RecoverInterruptedAsync(stoppingToken);
                    interruptedJobsRecovered = true;
                }

                await automationReadiness.WaitUntilReadyAsync(stoppingToken);
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

                try
                {
                    await Task.Delay(ErrorRetryDelay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}
