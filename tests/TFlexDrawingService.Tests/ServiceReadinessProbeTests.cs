using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Api.Data;
using TFlexDrawingService.Infrastructure.Configuration;
using TFlexDrawingService.Infrastructure.Persistence;
using TFlexDrawingService.Infrastructure.Storage;

namespace TFlexDrawingService.Tests;

public sealed class ServiceReadinessProbeTests
{
    [Fact]
    public async Task CheckAsync_RequiresCurrentWorkerHeartbeat()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        var templatesDirectory = Path.Combine(root, "templates");
        var storageDirectory = Path.Combine(root, "storage");
        Directory.CreateDirectory(templatesDirectory);
        Directory.CreateDirectory(storageDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(templatesDirectory, "template.grb"),
            "template");
        await File.WriteAllTextAsync(
            Path.Combine(templatesDirectory, "templates.json"),
            """
            {
              "templates": [
                {
                  "id": "template",
                  "code": "TEMPLATE",
                  "name": "Template",
                  "templateFilePath": "templates/template.grb",
                  "outputFormats": ["pdf"]
                }
              ]
            }
            """);

        var storageOptions = Options.Create(new DrawingStorageOptions
        {
            RootPath = storageDirectory,
            DatabasePath = Path.Combine(storageDirectory, "drawings.db")
        });
        var repository = new SqliteDrawingJobRepository(
            storageOptions,
            NullLogger<SqliteDrawingJobRepository>.Instance);
        await repository.InitializeAsync();
        var catalog = new JsonTemplateCatalog(
            Options.Create(new TemplateCatalogOptions
            {
                ProjectRootPath = root,
                ConfigPath = Path.Combine(templatesDirectory, "templates.json")
            }),
            NullLogger<JsonTemplateCatalog>.Instance);
        var probe = new ServiceReadinessProbe(
            catalog,
            repository,
            storageOptions);

        var withoutWorker = await probe.CheckAsync();

        Assert.False(withoutWorker.Ready);
        Assert.False(withoutWorker.Checks["worker"].Ready);
        Assert.True(withoutWorker.Checks["templates"].Ready);
        Assert.True(withoutWorker.Checks["database"].Ready);

        var heartbeatDirectory = Path.Combine(
            storageDirectory,
            ServiceReadinessProbe.WorkerHeartbeatDirectoryName);
        Directory.CreateDirectory(heartbeatDirectory);
        var heartbeatPath = Path.Combine(heartbeatDirectory, "worker-42.json");
        await File.WriteAllTextAsync(
            heartbeatPath,
            JsonSerializer.Serialize(new WorkerHeartbeat(
                true,
                42,
                DateTimeOffset.UtcNow,
                "ExternalProcess")));

        var withUnverifiedWorker = await probe.CheckAsync();

        Assert.False(withUnverifiedWorker.Ready);
        Assert.False(withUnverifiedWorker.Checks["worker"].Ready);

        var checkedAt = DateTimeOffset.UtcNow;
        await File.WriteAllTextAsync(
            heartbeatPath,
            JsonSerializer.Serialize(new WorkerHeartbeat(
                true,
                42,
                DateTimeOffset.UtcNow,
                "Mock",
                checkedAt,
                checkedAt + TimeSpan.FromMinutes(10))));

        var withWorker = await probe.CheckAsync();

        Assert.True(withWorker.Ready);
        Assert.True(withWorker.Checks.Values.All(check => check.Ready));
    }
}
