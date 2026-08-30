using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Infrastructure.Automation;
using TFlexDrawingService.Infrastructure.Configuration;
using TFlexDrawingService.Infrastructure.Persistence;

namespace TFlexDrawingService.Infrastructure.Services;

public sealed class TemplateAnalysisBackgroundService(
    TemplateAnalysisStore store,
    TemplateAnalysisProcessor processor,
    TFlexAutomationReadinessState automationReadiness,
    IOptions<DrawingQueueOptions> queueOptions,
    ILogger<TemplateAnalysisBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("T-FLEX template analysis worker started.");
        await store.RecoverInterruptedAsync(TimeSpan.FromMinutes(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await automationReadiness.WaitUntilReadyAsync(stoppingToken);
                var job = await store.TryClaimNextAsync(stoppingToken);
                if (job is null)
                {
                    await Task.Delay(queueOptions.Value.PollInterval, stoppingToken);
                    continue;
                }

                await processor.ProcessAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unhandled error in template analysis loop.");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }
}
