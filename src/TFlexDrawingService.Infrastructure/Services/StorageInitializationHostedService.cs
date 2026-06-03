using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Infrastructure.Services;

public sealed class StorageInitializationHostedService(
    IDrawingJobRepository repository,
    ITemplateCatalog templateCatalog,
    IOptions<DrawingStorageOptions> options,
    ILogger<StorageInitializationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.Value.RootPath);
        Directory.CreateDirectory(Path.Combine(options.Value.RootPath, "jobs"));
        Directory.CreateDirectory(Path.Combine(options.Value.RootPath, "generated"));

        await repository.InitializeAsync(cancellationToken);
        var templates = await templateCatalog.ListAsync(cancellationToken);

        logger.LogInformation("Loaded {TemplateCount} drawing template(s).", templates.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
