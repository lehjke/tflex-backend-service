using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Infrastructure.Automation;
using TFlexDrawingService.Infrastructure.Configuration;
using TFlexDrawingService.Infrastructure.Persistence;
using TFlexDrawingService.Infrastructure.Queue;
using TFlexDrawingService.Infrastructure.Services;
using TFlexDrawingService.Infrastructure.Storage;
using TFlexDrawingService.Tests.Support;

namespace TFlexDrawingService.Tests;

public sealed class DrawingJobProcessorTests
{
    [Fact]
    public async Task ProcessAsync_CopiesTemplateAndKeepsOriginalUnchanged()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        var templateDirectory = Path.Combine(root, "templates");
        Directory.CreateDirectory(templateDirectory);

        var templatePath = Path.Combine(templateDirectory, "template.grb");
        const string originalTemplateContent = "original template";
        await File.WriteAllTextAsync(templatePath, originalTemplateContent);
        var fragmentsDirectory = Path.Combine(templateDirectory, "template");
        Directory.CreateDirectory(fragmentsDirectory);
        await File.WriteAllTextAsync(Path.Combine(fragmentsDirectory, "fragment.grb"), "fragment");

        var storageOptions = Options.Create(new DrawingStorageOptions
        {
            RootPath = Path.Combine(root, "storage"),
            DatabasePath = Path.Combine(root, "storage", "drawings.db")
        });

        var repository = new SqliteDrawingJobRepository(
            storageOptions,
            NullLogger<SqliteDrawingJobRepository>.Instance);
        await repository.InitializeAsync();

        var queue = new SqliteDrawingJobQueue(
            repository,
            Options.Create(new DrawingQueueOptions { PollInterval = TimeSpan.FromMilliseconds(10) }),
            NullLogger<SqliteDrawingJobQueue>.Instance);

        var template = new DrawingTemplate
        {
            Id = "sample",
            Code = "sample",
            Name = "Sample",
            TemplateFilePath = templatePath,
            OutputFormats = ["pdf"],
            Parameters = []
        };

        var job = new DrawingJob
        {
            TemplateId = template.Id,
            OutputFormat = "pdf",
            InputParametersJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["WIDTH"] = 1000
            })
        };

        await queue.EnqueueAsync(job);
        var claimedJob = await queue.DequeueAsync(CancellationToken.None);

        var processor = new DrawingJobProcessor(
            new InMemoryTemplateCatalog(template),
            repository,
            new LocalFileStorage(storageOptions),
            new MockTFlexAutomationClient(NullLogger<MockTFlexAutomationClient>.Instance),
            NullLogger<DrawingJobProcessor>.Instance);

        await processor.ProcessAsync(claimedJob);

        var savedJob = await repository.GetAsync(job.Id);

        Assert.NotNull(savedJob);
        Assert.Equal(DrawingJobStatus.Completed, savedJob.Status);
        Assert.Single(savedJob.ResultFiles);
        Assert.True(File.Exists(savedJob.ResultFiles[0].Path));
        Assert.Equal(originalTemplateContent, await File.ReadAllTextAsync(templatePath));
        Assert.True(File.Exists(Path.Combine(savedJob.WorkingDirectory!, "template.grb")));
        Assert.True(File.Exists(Path.Combine(savedJob.WorkingDirectory!, "template", "fragment.grb")));
    }
}
