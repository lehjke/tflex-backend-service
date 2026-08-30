using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Infrastructure.Configuration;
using TFlexDrawingService.Infrastructure.Persistence;

namespace TFlexDrawingService.Infrastructure.Services;

public sealed class StorageInitializationHostedService(
    IDrawingJobRepository repository,
    ITemplateCatalog templateCatalog,
    TemplateAnalysisStore templateAnalysisStore,
    IOptions<DrawingStorageOptions> options,
    ILogger<StorageInitializationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.Value.RootPath);
        Directory.CreateDirectory(Path.Combine(options.Value.RootPath, "jobs"));
        Directory.CreateDirectory(Path.Combine(options.Value.RootPath, "generated"));
        Directory.CreateDirectory(Path.Combine(options.Value.RootPath, "template-analysis"));

        await repository.InitializeAsync(cancellationToken);
        await templateAnalysisStore.InitializeAsync(cancellationToken);
        var templates = await templateCatalog.ListAsync(cancellationToken);

        logger.LogInformation("Loaded {TemplateCount} drawing template(s).", templates.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
